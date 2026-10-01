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
    public static StepMachineOutcome StartPhase(StepPlan plan, ControlMode control = ControlMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var events = new List<GameEvent>(capacity: 4)
        {
            new PhaseStartedEvent { Plan = plan, Control = control },
        };
        var state = Apply(null, events[0])
            ?? throw new InvalidOperationException("事件流损坏：开启阶段没有产出步骤机状态");
        if (!state.IsPlanCompleted)
        {
            EnterCurrentSlot(state, events);
        }
        else
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = plan.Label });
        }

        return AppliedFromNothing(events);
    }

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

        return input switch
        {
            SlotQuotaElapsedInput => HandleQuotaElapsed(state),
            SubmitResponseInput response => HandleResponse(state, context, response),
            VoidRequestInput voidRequest => HandleVoid(state, voidRequest),
            ForceAdvanceInput forceAdvance => HandleForceAdvance(state, forceAdvance),
            TakeOverInput takeOver => HandleControlChange(state, ControlMode.StorytellerTakeover, takeOver.Reason),
            ReleaseControlInput release => HandleControlChange(state, ControlMode.Automatic, release.Reason),
            SeatStateChangedInput seatChanged => HandleSeatStateChanged(state, seatChanged),
            ResolveDecisionPointInput resolve => HandleDecisionResolved(state, context, resolve),
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

    private static StepMachineOutcome HandleQuotaElapsed(StepMachineState state)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        if (state.Quota == SlotQuotaState.Elapsed)
        {
            return Applied(state, []);
        }

        var events = new List<GameEvent>
        {
            new SlotQuotaElapsedEvent { SlotId = state.CurrentSlot!.Id },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleResponse(
        StepMachineState state,
        SettlementContext context,
        SubmitResponseInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

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

        if (!pending.Prompt.Options.Any(option => string.Equals(option.Value, input.OptionValue, StringComparison.Ordinal)))
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
        }

        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleVoid(StepMachineState state, VoidRequestInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

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
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleForceAdvance(StepMachineState state, ForceAdvanceInput input)
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
        AppendForceAdvance(afterHolds, events, input.Reason);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleControlChange(StepMachineState state, ControlMode mode, string reason)
    {
        if (state.Control == mode)
        {
            return Reject(state, StepMachineRejectionReason.ControlModeUnchanged, $"控制模式已经是 {mode}");
        }

        var events = new List<GameEvent>
        {
            new ControlModeChangedEvent { Mode = mode, Reason = reason },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleSeatStateChanged(StepMachineState state, SeatStateChangedInput input)
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
        return Applied(state, WithAutoAdvance(state, events));
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

        var events = new List<GameEvent>
        {
            new DecisionPointResolvedEvent
            {
                DecisionPointId = input.DecisionPointId,
                Decision = input.Decision,
                Note = input.Note,
            },
        };

        // 裁定点属于行动槽位时，裁定本身可能就是这一步的结算输入：
        // 入口裁定（该步没有玩家选择）与选择后裁定都在这里收口。
        if (state.CurrentSlot is { } slot)
        {
            var choice = state.PendingRequest is
            {
                Status: OperationRequestStatus.Answered,
                Answer: { } answer,
            } pending && pending.SlotId == slot.Id
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
        }

        return Applied(state, WithAutoAdvance(state, events));
    }

    private static List<GameEvent> WithAutoAdvance(StepMachineState state, List<GameEvent> events)
    {
        var after = StepMachineFolder.ApplyAll(state, events)
            ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态");
        if (CanAutoAdvance(after))
        {
            AppendAdvance(after, events);
        }

        return events;
    }

    private static bool CanAutoAdvance(StepMachineState state) =>
        !state.IsPlanCompleted
        && state.Control == ControlMode.Automatic
        && state.Quota == SlotQuotaState.Elapsed
        && !state.IsHeld;

    private static void AppendAdvance(StepMachineState state, List<GameEvent> events)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotAdvancedEvent { FromIndex = from, ToIndex = to });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        EnterCurrentSlot(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            events);
    }

    private static void AppendForceAdvance(StepMachineState state, List<GameEvent> events, string reason)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotForceAdvancedEvent { FromIndex = from, ToIndex = to, Reason = reason });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        EnterCurrentSlot(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            events);
    }

    private static void EnterCurrentSlot(StepMachineState state, List<GameEvent> events)
    {
        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("进入槽位失败：计划已走完");

        events.Add(new SlotEnteredEvent { SlotIndex = state.SlotIndex, SlotId = slot.Id });

        if (slot.Kind != StepSlotKind.Action)
        {
            return;
        }

        if (slot.Actor is null || slot.Prompt is null)
        {
            events.Add(new SlotBlockedEvent
            {
                SlotId = slot.Id,
                Reason = "行动槽位缺少行动者或选择契约（数据缺陷）——说书人可强推 / 接管 / 重建",
            });
            return;
        }

        switch (slot.Prompt.Evaluate())
        {
            case DecisionPointOutcome.AwaitingChoice:
                events.Add(new OperationRequestIssuedEvent { Request = BuildRequest(state, slot) });
                break;
            case DecisionPointOutcome.Skipped:
                events.Add(new PromptSkippedEvent
                {
                    SlotId = slot.Id,
                    Reason = "无合法选项：按声明的 Skip 走（R-0009），配额照走",
                });
                break;
            case DecisionPointOutcome.StorytellerDecides:
                events.Add(new DecisionPointRaisedEvent
                {
                    SlotId = slot.Id,
                    DecisionPoint = new DecisionPoint
                    {
                        Id = AbilitySettlement.DecisionPointIdOf(state, slot),
                        Prompt = slot.Prompt,
                    },
                });
                break;
            default:
                events.Add(new SlotBlockedEvent { SlotId = slot.Id, Reason = slot.Prompt.Context });
                break;
        }
    }

    private static OperationRequest BuildRequest(StepMachineState state, StepSlot slot) =>
        new()
        {
            Id = new OperationRequestId($"{state.Plan.Label}:{slot.Id}"),
            Addressee = slot.Actor!.Value,
            SlotId = slot.Id,
            PlanLabel = state.Plan.Label,
            IssuedAtSlotIndex = state.SlotIndex,
            Prompt = slot.Prompt!,
            Dependencies = slot.Dependencies,
        };

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态"),
            Events = events,
        };

    private static StepMachineOutcome AppliedFromNothing(List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(null, events)
                ?? throw new InvalidOperationException("事件流为空：无法从空事件重建状态"),
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
