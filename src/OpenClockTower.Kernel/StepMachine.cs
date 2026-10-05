namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机：驱动阶段与夜晚顺序表推进、生成操作请求与裁定点，并能在挂起点暂停。
/// </summary>
/// <remarks>
/// <para>
/// **纯计算**（D-0008）：给定（状态 + 输入）必产出同一结果。时间不是内核的输入来源——
/// 配额走完、座位状态变化都是上层送进来的输入；内核只产出事件。
/// </para>
/// <para>
/// **挂起语义**（D-0011 / D-0013）：槽位配额是**最短**时间，自动推进需要两个条件同时满足——
/// 配额已走完（<see cref="SlotQuotaState.Elapsed"/>）且没有挂起。请求没有超时：
/// 玩家一直不响应，槽位就一直不推进（玩家响应慢会拉长夜晚，属 D-0013 已登记的边界）。
/// </para>
/// <para>
/// **兜底语义**（D-0014）：<see cref="ForceAdvanceInput"/> 越过配额与挂起直接推进，
/// <see cref="TakeOverInput"/> 暂停自动推进；每一次越过都产出事件、可审计、可重放。
/// 挂起、阻塞、逻辑出错都不构成死锁——兜底入口永远开着。
/// </para>
/// <para>
/// <see cref="Handle"/> 与 <see cref="Apply"/> 成对：前者产出事件，后者把事件折叠回状态；
/// 测试断言两者一致，重放（重启恢复、断线补齐）因此可信。
/// </para>
/// <para>
/// **结算**：行动槽位的「玩家选完 / 说书人裁完」由 <see cref="AbilitySettlement"/> 收口——
/// 它读状态账判生效、按槽位角色取契约产出事件；信息类能力会在这里再挂一次
/// <see cref="DecisionPoint"/>（D-0002）。判不了（账不全）时整条输入被拒绝，不猜（D-0015）。
/// </para>
/// </remarks>
public static class StepMachine
{
    /// <summary>开启一个新阶段：按计划进入第一个槽位，必要时发出操作请求或裁定点。</summary>
    /// <remarks>
    /// 真实对局必须用带 <paramref name="previous"/> 的重载：新阶段要**接在现有状态上**开始，
    /// 否则白天账（死亡玩家票权 / 逐日事实）会被丢掉。不带前一状态的版本供全新对局与测试夹具使用。
    /// </remarks>
    public static StepMachineOutcome StartPhase(StepPlan plan, ControlMode control = ControlMode.Automatic) =>
        StartPhase(plan, previous: null, GameState.Empty, control);

    /// <summary>开启一个新阶段，并保留现有状态里的非计划账（尤其白天账与已消耗票权）。</summary>
    public static StepMachineOutcome StartPhase(
        StepPlan plan,
        StepMachineState? previous,
        ControlMode control = ControlMode.Automatic) =>
        StartPhase(plan, previous, GameState.Empty, control);

    /// <summary>
    /// 开启一个新阶段，并带上**当前状态账**：进入槽位时按它确认行动者是否还站得住
    /// （说书人杀掉了尚未唤醒的恶魔、或行动者的角色在夜里被换走时，这一格不再唤醒他；
    /// 见 <see cref="StepSlotEntry.Enter"/> 与 rulings.md R-0030）。
    /// </summary>
    public static StepMachineOutcome StartPhase(
        StepPlan plan,
        StepMachineState? previous,
        GameState ledger,
        ControlMode control = ControlMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);

        var events = new List<GameEvent>(capacity: 4)
        {
            new PhaseStartedEvent { Plan = plan, Control = control },
        };
        var state = Apply(previous, events[0])
            ?? throw new InvalidOperationException("事件流损坏：开启阶段没有产出步骤机状态");
        if (!state.IsPlanCompleted)
        {
            // 首个槽位按夜晚顺序表固定是节拍类（黄昏 / 爪牙信息 / 恶魔信息 / …），不是角色行动槽：
            // 这里没有实时提示来源可用，走计划快照；刷新发生在推进路径（AppendAdvance / AppendForceAdvance）。
            StepSlotEntry.Enter(state, ledger, [], prompts: null, events);
        }
        else
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = plan.Label });
        }

        return new StepMachineOutcome
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(previous, events)
                ?? throw new InvalidOperationException("事件流损坏：开启阶段没有产出步骤机状态"),
            Events = events,
        };
    }

    /// <summary>
    /// 开启一个白天阶段：进入唯一的白天窗口槽位（不消耗配额、不自动推进），并让白天账新开一天。
    /// </summary>
    /// <remarks>
    /// 计划由应用层构造（标签 <c>sv:day-N</c>）；形状与天数的校验、事件产出在
    /// <see cref="DayStepMachine"/>（拆出以守单文件 600 行门禁：一个类不该同时装昼夜两套推进）。
    /// 真实对局走带 <paramref name="previous"/> 的重载，保证跨天票权与逐日事实保留。
    /// </remarks>
    public static StepMachineOutcome StartDay(
        StepPlan plan,
        int dayNumber,
        ControlMode control = ControlMode.Automatic) =>
        StartDay(plan, dayNumber, previous: null, control);

    /// <summary>开启白天，并保留现有状态里的白天账（跨天票权 / 逐日事实）。</summary>
    public static StepMachineOutcome StartDay(
        StepPlan plan,
        int dayNumber,
        StepMachineState? previous,
        ControlMode control = ControlMode.Automatic) =>
        DayStepMachine.StartDay(plan, dayNumber, previous, control);

    /// <summary>
    /// 处理一条输入（无结算上下文）：等价于传 <see cref="SettlementContext.Empty"/>——
    /// 只推进步骤机、不产出结算事件，供只关心推进的夹具使用。
    /// </summary>
    public static StepMachineOutcome Handle(StepMachineState state, StepMachineInput input) =>
        Handle(state, SettlementContext.Empty, input);

    /// <summary>处理一条输入；非法输入被拒绝而不抛异常。结算要读账与契约，走 <paramref name="context"/>。</summary>
    public static StepMachineOutcome Handle(
        StepMachineState state,
        SettlementContext context,
        StepMachineInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        // 游戏已经结束：一切输入都被拒（R-0024）——包括说书人命令与状态观测，
        // 撤销只能走截断重放（D-0010），不能继续往一条已经结束的流上追加操作。
        if (state.Outcome is { } outcome)
        {
            return Reject(
                state,
                StepMachineRejectionReason.GameEnded,
                $"本局已经结束（{outcome.Condition}：{outcome.Detail}），不能再接受输入");
        }

        return input switch
        {
            SlotQuotaElapsedInput => HandleQuotaElapsed(state, context),
            SubmitResponseInput response => HandleResponse(state, context, response),
            VoidRequestInput voidRequest => HandleVoid(state, context, voidRequest),
            ForceAdvanceInput forceAdvance when state.Plan.Phase == GamePhase.Day
                => DayStepMachine.ForceAdvance(state, forceAdvance),
            ForceAdvanceInput forceAdvance => HandleForceAdvance(state, context, forceAdvance),
            TakeOverInput takeOver
                => HandleControlChange(state, context, ControlMode.StorytellerTakeover, takeOver.Reason),
            ReleaseControlInput release
                => HandleControlChange(state, context, ControlMode.Automatic, release.Reason),
            SeatStateChangedInput seatChanged => HandleSeatStateChanged(state, context, seatChanged),
            ResolveDecisionPointInput resolve => HandleDecisionResolved(state, context, resolve),
            AskArtistQuestionInput artistQuestion => ArtistQuestionMachine.Ask(state, context, artistQuestion),
            AskSavantQuestionInput savantQuestion => SavantQuestionMachine.Ask(state, context, savantQuestion),
            PunishExecutionInput punish => AdjudicatedExecutionMachine.Handle(state, context, punish),
            PitHagCasualtyInput casualty => PitHagNightMachine.HandleCasualty(state, context, casualty),
            ResolveDeferredDeathInput deferredDeath => PitHagNightMachine.HandleResolve(state, context, deferredDeath),
            StepMachineInput dayInput when DayStepMachine.IsDayInput(dayInput)
                => DayStepMachine.Handle(state, context, dayInput),
            _ => Reject(state, StepMachineRejectionReason.UnexpectedInput, $"未知输入：{input.GetType().Name}"),
        };
    }

    /// <summary>
    /// 把一条事件折叠回状态——重放、重启恢复、撤销的共同基础。
    /// </summary>
    /// <returns>
    /// 折叠后的状态；账事件（座位状态 / 效果 / 疯狂要求）不创建状态，阶段未开始时为 null。
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 事件流顺序损坏时抛出：恢复必须**显式失败**，绝不静默继续（D-0014 能力 3）。
    /// </exception>
    public static StepMachineState? Apply(StepMachineState? state, GameEvent gameEvent) =>
        StepMachineFolder.Apply(state, gameEvent);

    /// <summary>从事件流重建状态（重放、重启恢复、撤销的基础）。</summary>
    /// <returns>事件流里还没有任何阶段事件时返回 null（例如只有开局分配与初始状态）。</returns>
    public static StepMachineState? Fold(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return StepMachineFolder.ApplyAll(null, events);
    }

    private static StepMachineOutcome HandleQuotaElapsed(StepMachineState state, SettlementContext context)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        if (state.Quota == SlotQuotaState.Elapsed)
        {
            return Applied(state, []);
        }

        if (state.CurrentSlot is { Kind: StepSlotKind.DayWindow })
        {
            // 白天窗口不消耗配额：宿主节拍器不应为它送配额输入；这里再兜一层，不产出事件、不推进。
            return DayStepMachine.QuotaElapsed(state);
        }

        var events = new List<GameEvent>
        {
            new SlotQuotaElapsedEvent { SlotId = state.CurrentSlot!.Id },
        };
        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleResponse(
        StepMachineState state,
        SettlementContext context,
        SubmitResponseInput input)
    {
        var pending = state.PendingRequest;
        if (pending is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingRequest, "当前没有等待响应的请求");
        }

        if (pending.Id != input.RequestId)
        {
            return Reject(state, StepMachineRejectionReason.NotCurrentRequest, $"当前挂起的是 {pending.Id}");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            return Reject(state, StepMachineRejectionReason.RequestAlreadyResolved, "请求已经了结");
        }

        if (!pending.Prompt.IsLegalAnswer(input.OptionValue))
        {
            return Reject(state, StepMachineRejectionReason.OptionNotLegal, $"选项不在合法集合里：{input.OptionValue}");
        }

        var events = new List<GameEvent>
        {
            new OperationRequestAnsweredEvent
            {
                RequestId = pending.Id,
                Answer = new OperationRequestAnswer
                {
                    OptionValue = input.OptionValue,
                    Source = input.Source,
                    Note = input.Note,
                },
            },
        };

        // 触发来源的请求（如呆瓜死亡选择，R-0027）不落在任何槽位上：后果由事件触发管线
        // 从这条"答了什么"的事件里产出；因此它也不受"计划是否走完"约束——
        // 呆瓜的选择常常正好开在白天关闭（计划走完）之后、下一夜开始之前。
        if (pending.Origin.Kind != OperationRequestOriginKind.Slot)
        {
            return Applied(state, events);
        }

        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        // 玩家选完 → 结算：要么直接产出事件，要么再挂一次「信息类裁定」（D-0002）。
        if (state.CurrentSlot is { } slot)
        {
            var settlement = AbilitySettlement.Plan(
                slot,
                state,
                context,
                input.OptionValue,
                decision: null);
            switch (settlement.Kind)
            {
                case AbilitySettlementPlan.PlanKind.Indeterminate:
                    return Reject(
                        state,
                        StepMachineRejectionReason.LedgerIncomplete,
                        settlement.FailureNote!);
                case AbilitySettlementPlan.PlanKind.RequiresDecision when settlement.DecisionPrompt is { } prompt:
                    events.Add(new DecisionPointRaisedEvent
                    {
                        SlotId = slot.Id,

                        // 归属 = 这一步的行动者：说书人看着他的席位就知道"谁在等"。
                        AttributionSeat = slot.Actor,
                        DecisionPoint = new DecisionPoint
                        {
                            Id = AbilitySettlement.DecisionPointIdOf(state, slot),
                            Prompt = prompt,
                        },
                    });
                    return Applied(state, events);
                case AbilitySettlementPlan.PlanKind.Resolved:
                    events.AddRange(settlement.Events);
                    break;
                default:
                    break;
            }

            // 触发格（理发师格等）：结算不在槽位契约里，而由触发管线在同一批的**批末**产出。
            // 若在这里照常自动推进，触发管线读到的"当前槽位"已经越过这一格——换角重绑会漏掉紧邻的
            // 下一格；而且答在配额前 / 后会得到不同的重绑结果（R-0032 的「尚未进入」判据被计时左右）。
            // 因此答完后**重进本格**：节拍重新起算、由下一次配额输入推进；触发管线在同一批里先完成
            // 重绑与收口，下一格在之后的批里以当时的账进入（入口检查也读到重绑后的行动者）。
            if (slot.Kind == StepSlotKind.Trigger)
            {
                events.Add(new SlotEnteredEvent { SlotIndex = state.SlotIndex, SlotId = slot.Id });
                return Applied(state, events);
            }
        }

        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleVoid(
        StepMachineState state,
        SettlementContext context,
        VoidRequestInput input)
    {
        var pending = state.PendingRequest;
        if (pending is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingRequest, "当前没有等待响应的请求");
        }

        if (pending.Id != input.RequestId)
        {
            return Reject(state, StepMachineRejectionReason.NotCurrentRequest, $"当前挂起的是 {pending.Id}");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            return Reject(state, StepMachineRejectionReason.RequestAlreadyResolved, "请求已经了结");
        }

        var events = new List<GameEvent>
        {
            new OperationRequestVoidedEvent
            {
                RequestId = pending.Id,
                Void = new OperationRequestVoid { Reason = input.Reason, Note = input.Note },
            },
        };

        // 触发来源的请求（呆瓜的公开选择，R-0027）不落在任何槽位上：它可以在计划走完之后开出，
        // 因此这里必须与 HandleResponse **同款旁路**——否则那种请求答得了（代填走 HandleResponse）、
        // 却撤不掉（作废与强推都被 IsPlanCompleted 挡住），说书人的兜底入口就关死了
        // （胜负票独立复核 F-4；D-0011 / D-0014 要求兜底入口永远开着）。
        if (pending.Origin.Kind != OperationRequestOriginKind.Slot)
        {
            return Applied(state, events);
        }

        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleForceAdvance(
        StepMachineState state,
        SettlementContext context,
        ForceAdvanceInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        var events = new List<GameEvent>();

        if (state.PendingRequest is { Status: OperationRequestStatus.Pending } pending)
        {
            events.Add(new OperationRequestVoidedEvent
            {
                RequestId = pending.Id,
                Void = new OperationRequestVoid
                {
                    Reason = OperationRequestVoidReason.StorytellerTakeover,
                    Note = input.Reason,
                },
            });
        }

        if (state.AwaitingDecision is { } decision)
        {
            events.Add(new DecisionPointResolvedEvent
            {
                DecisionPointId = decision.Id,
                Decision = null,
                Note = $"强推：{input.Reason}",
            });
        }

        var afterHolds = StepMachineFolder.ApplyAll(state, events)
            ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态");
        StepSlotEntry.AppendForceAdvance(afterHolds, context, events, input.Reason);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleControlChange(
        StepMachineState state,
        SettlementContext context,
        ControlMode mode,
        string reason)
    {
        if (state.Control == mode)
        {
            return Reject(state, StepMachineRejectionReason.ControlModeUnchanged, $"控制模式已经是 {mode}");
        }

        var events = new List<GameEvent>
        {
            new ControlModeChangedEvent { Mode = mode, Reason = reason },
        };
        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleSeatStateChanged(
        StepMachineState state,
        SettlementContext context,
        SeatStateChangedInput input)
    {
        if (input.Life is null
            && input.Character is null
            && input.Alignment is null
            && input.Drunk is null
            && input.Poison is null)
        {
            return Reject(
                state,
                StepMachineRejectionReason.UnexpectedInput,
                "座位状态变化至少要给出一个观测维度");
        }

        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                Seat = input.Seat,
                Life = input.Life,
                Character = input.Character,
                Alignment = input.Alignment,
                Drunk = input.Drunk,
                Poison = input.Poison,
                Reason = input.Reason,
                CausedBy = input.CausedBy,
            },
        };

        var pending = state.PendingRequest;
        if (pending is null || pending.Status != OperationRequestStatus.Pending)
        {
            return Applied(state, events);
        }

        var violated = SeatDependencyCheck.FirstViolated(pending.Dependencies, input);
        if (violated is null)
        {
            return Applied(state, events);
        }

        events.Add(new OperationRequestVoidedEvent
        {
            RequestId = pending.Id,
            Void = new OperationRequestVoid
            {
                Reason = OperationRequestVoidReason.DependencyViolated,
                Note = SeatDependencyCheck.Describe(violated, input),
            },
        });
        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleDecisionResolved(
        StepMachineState state,
        SettlementContext context,
        ResolveDecisionPointInput input)
    {
        if (state.AwaitingDecision is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingDecision, "当前没有等待说书人的裁定点");
        }

        if (state.AwaitingDecision.Id != input.DecisionPointId)
        {
            return Reject(
                state,
                StepMachineRejectionReason.NotCurrentRequest,
                $"当前挂起的是 {state.AwaitingDecision.Id}");
        }

        // 艺术家的白天提问（R-0040）：裁定点属于问题——回答 / 要求重问在这里结清，
        // 与槽位结算无关（白天窗口不是角色行动槽位）。
        if (state.ArtistQuestion is { } artistQuestion
            && state.AwaitingDecision.Id == ArtistQuestionMachine.DecisionIdOf(state))
        {
            return ArtistQuestionMachine.Resolve(
                state,
                context,
                artistQuestion,
                input.Decision,
                input.Note);
        }

        // 博学者的白天提问（R-0057）：与艺术家同族，两条信息由说书人给。
        if (state.SavantQuestion is { } savantQuestion
            && state.AwaitingDecision.Id == SavantQuestionMachine.DecisionIdOf(state))
        {
            return SavantQuestionMachine.Resolve(
                state,
                context,
                savantQuestion,
                input.Decision,
                input.Note);
        }

        var events = new List<GameEvent>
        {
            new DecisionPointResolvedEvent
            {
                DecisionPointId = input.DecisionPointId,
                Decision = input.Decision,
                Note = input.Note,
            },
        };

        // 裁定点按**归属**收口，不按"当前有没有槽位"猜：
        // ① 属于当前行动槽位的裁定点（入口裁定 / 选择后裁定）：裁定本身是这一步的结算输入，
        //    在这里结算，随后照常自动推进；
        // ② 触发器开出的裁定点（如理发师死亡触发的「哪名恶魔执行交换」）：这里只落裁定本身——
        //    它的续推动作（开请求 / 收口事实）由同一批的触发管线产出；在这里替它推进计划，
        //    会让尚未开出的后续请求错过自己的槽位（触发来源请求的旁路口径见 HandleResponse）。
        // 「结清后没有任何后续挂起」的收口路径（贤者展示 / 心上人醉酒）由提交管线补一步
        // 「重进本格」复位配额（见 SessionCommit.BuildDecisionContinuation，独立复核 H-1）——
        // 放在那里而不是这里，是因为只有**触发管线跑完之后**才知道有没有后续请求。
        var slot = state.CurrentSlot;
        if (slot is null || AbilitySettlement.DecisionPointIdOf(state, slot) != input.DecisionPointId)
        {
            return Applied(state, events);
        }

        var choice = state.PendingRequest is
        {
            Status: OperationRequestStatus.Answered,
            Answer: { } answer,
        } pending && pending.Origin.SlotId is { } originSlotId && originSlotId == slot.Id
            ? answer.OptionValue
            : null;

        var settlement = AbilitySettlement.Plan(slot, state, context, choice, input.Decision);
        switch (settlement.Kind)
        {
            case AbilitySettlementPlan.PlanKind.Indeterminate:
                return Reject(
                    state,
                    StepMachineRejectionReason.LedgerIncomplete,
                    settlement.FailureNote!);
            case AbilitySettlementPlan.PlanKind.RequiresDecision:
                return Reject(
                    state,
                    StepMachineRejectionReason.UnexpectedInput,
                    "结算契约在已有裁定之后仍要求再说书人裁定一次（契约缺陷）");
            case AbilitySettlementPlan.PlanKind.Resolved:
                events.AddRange(settlement.Events);
                break;
            default:
                break;
        }

        StepSlotEntry.AutoAdvance(state, context, events);
        return Applied(state, events);
    }

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态"),
            Events = events,
        };

    private static StepMachineOutcome Reject(
        StepMachineState state,
        StepMachineRejectionReason reason,
        string note) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Rejected,
            State = state,
            Events = [],
            RejectionReason = reason,
            RejectionNote = note,
        };
}
