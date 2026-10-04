namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位的进入与推进：把一个槽位翻译成事件（请求 / 裁定点 / 跳过 / 阻塞），以及推进到下一个槽位。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 拆出（单文件 600 行门禁）：步骤机负责"处理输入、派生后果"，
/// 这里只回答"进入第 k 个槽位要产出什么"——纯事件构造，无状态、无 IO（D-0008）。
/// </para>
/// <para>
/// 空槽位与节拍槽位只产出 <see cref="SlotEnteredEvent"/>（照样消耗配额，D-0013 §1）；
/// 行动槽位按选择契约的求值结果分成请求 / 跳过 / 裁定点 / 阻塞四路（R-0009）。
/// </para>
/// </remarks>
internal static class StepSlotEntry
{
    /// <summary>自动推进条件：计划未完、非白天窗口、自动控制、配额已走完且没有挂起。</summary>
    internal static bool CanAutoAdvance(StepMachineState state) =>
        !state.IsPlanCompleted
        && state.CurrentSlot?.Kind != StepSlotKind.DayWindow
        && state.Control == ControlMode.Automatic
        && state.Quota == SlotQuotaState.Elapsed
        && !state.IsHeld;

    /// <summary>
    /// 槽位工作完成后的一次推进机会：满足自动推进条件时，「行动两次」窗口生效 → 重进本格
    /// （第二次结算），否则推进到下一格。两个出口都在本方法里，调用方只管把本批事件交进来。
    /// </summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0052 第 2 条：咖啡师效果 2 的第二次结算发生在
    /// **同一槽位的下一次进入**，配额重新起算；请求 / 裁定标识带遍次（<see cref="SlotKeyOf"/>）。
    /// </remarks>
    internal static void AutoAdvance(StepMachineState state, SettlementContext context, List<GameEvent> events)
    {
        var after = StepMachineFolder.ApplyAll(state, events)
            ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态");
        if (!CanAutoAdvance(after))
        {
            return;
        }

        if (SecondActionSettlement.IsDue(after, context, events))
        {
            AppendSecondPass(after, context, events);
            return;
        }

        AppendAdvance(after, context, events);
    }

    /// <summary>
    /// 「行动两次」的第二次结算：重进本格——同一槽位再走一遍进入流程（新请求 / 新裁定点，
    /// 标识带遍次），配额重新起算，由下一次输入推进。
    /// </summary>
    /// <remarks>
    /// 进入前用**折完本批事件之后的账**再确认一次行动者还站得住：第一遍的效果可能已经换掉他的角色
    /// （舞蛇人换角）或杀死他，第二遍就不该再唤醒（跳过并记原因，直接推进）——与入槽检查同一把尺子，
    /// 只是口径更保守（第一遍确实已经落地）。
    /// </remarks>
    internal static void AppendSecondPass(StepMachineState state, SettlementContext context, List<GameEvent> events)
    {
        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("重进槽位失败：计划已走完");

        if (UnavailableReason(SecondActionSettlement.Fold(context.State, events), slot) is { } unavailable)
        {
            events.Add(new PromptSkippedEvent
            {
                SlotId = slot.Id,
                Reason = $"第二次结算前的再确认：{unavailable}（咖啡师「行动两次」不重开，R-0052 第 5 条）",
            });
            AppendAdvance(state, context, events);
            return;
        }

        // 遍次先折进一份临时状态，再交给 Enter 产出进入事件（Enter 自己会追加 SlotEnteredEvent，
        // 这里不能重复追加）：请求 / 裁定标识要读到"这是第 2 遍"。
        var entered = StepMachineFolder.Apply(
            state,
            new SlotEnteredEvent { SlotIndex = state.SlotIndex, SlotId = slot.Id })
            ?? throw new InvalidOperationException("事件流损坏：重进槽位后丢失步骤机状态");

        Enter(entered, context.State, context.Seats, context.SlotPrompts, events);
    }

    /// <summary>产出一条自动推进事件；推进后进入新槽位（计划走完则补阶段完成事件）。</summary>
    internal static void AppendAdvance(StepMachineState state, SettlementContext context, List<GameEvent> events)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        AppendPitHagNightClose(state, context.State, events);
        AppendBarberNightClose(state, events, to);
        AppendSageNightClose(state, events, to);

        events.Add(new SlotAdvancedEvent { FromIndex = from, ToIndex = to });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            context.State,
            context.Seats,
            context.SlotPrompts,
            events);
    }

    /// <summary>产出一条强推事件（说书人兜底，D-0014）；推进后进入新槽位。</summary>
    internal static void AppendForceAdvance(
        StepMachineState state,
        SettlementContext context,
        List<GameEvent> events,
        string reason)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        AppendPitHagNightClose(state, context.State, events);
        AppendBarberNightClose(state, events, to);
        AppendSageNightClose(state, events, to);

        events.Add(new SlotForceAdvancedEvent { FromIndex = from, ToIndex = to, Reason = reason });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            context.State,
            context.Seats,
            context.SlotPrompts,
            events);
    }

    /// <summary>
    /// 麻脸巫婆之夜的窗口收口：当前槽位已经走到（或越过）最后一个「能造成死亡的恶魔行动」时，
    /// 把仍未裁定的待定死亡按恶魔攻击的自然结果生效，并关闭窗口。
    /// </summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0030 第 1、3 条：窗口到「最后一个能够造成死亡的恶魔
    /// 行动结束后」为止；未裁定的按默认结果生效，并**显式**记一条说明（不是静默默认）。
    /// 收口事件排在推进事件之前，因此不影响"最后一条事件"的折叠口径。
    /// </remarks>
    private static void AppendPitHagNightClose(StepMachineState state, GameState ledger, List<GameEvent> events)
    {
        if (state.PitHagNight is not { } night || state.SlotIndex < night.ClosesAfterSlotIndex)
        {
            return;
        }

        foreach (var deferred in night.Deferred)
        {
            events.Add(new DeferredDeathResolvedEvent
            {
                Target = deferred.Target,
                Killed = true,
                Note = deferred.Transformation is null
                    ? "窗口关闭时仍未裁定：按恶魔攻击的自然结果生效（rulings.md R-0030 第 3 条）"
                    : "窗口关闭时仍未裁定：方古的侵染按自然结果生效——外来者变成新的邪恶方古、"
                        + "原方古死亡（rulings.md R-0030 第 3 条 / R-0034）",
            });
            PitHagNightMachine.AppendOutcome(events, ledger, deferred, "窗口关闭时未裁定");
        }

        events.Add(new PitHagNightClosedEvent
        {
            Note = "麻脸巫婆之夜的死亡裁量窗口已关闭（最后一名能造成死亡的恶魔行动结束）："
                + $"未裁定的待定死亡 {night.Deferred.Count} 条按默认结果生效",
        });
    }

    /// <summary>
    /// 「今晚理发」事实的收口（过时不候）：夜晚计划走完时仍未消费的事实显式清空。
    /// </summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0033：事实跨白天 → 夜晚保留（白天死亡当夜交互），
    /// 到夜晚 → 白天边界仍未消费时**显式**记「过时不候」再清空——不顺延到下一夜，也不是静默丢弃。
    /// 只对夜晚计划收口：白天计划走完不碰这个事实（它本来就属于当夜）。
    /// </remarks>
    private static void AppendBarberNightClose(StepMachineState state, List<GameEvent> events, int toIndex)
    {
        if (state.BarberNight is not { } night || toIndex < state.Plan.Slots.Count)
        {
            return;
        }

        if (state.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
        {
            return;
        }

        events.Add(new BarberNightClosedEvent
        {
            Note = $"过时不候：理发师（{night.Source.Value} 号）死亡触发的恶魔交互没有被消费，"
                + "夜晚结束时清空（事实不顺延到下一夜；平台口径见 rulings.md R-0033）",
        });
    }

    /// <summary>
    /// 贤者事实的收口（过时不候）：夜晚计划走完时仍未消费的事实显式清空。
    /// </summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0038：贤者死于恶魔击杀总发生在当夜贤者格之前
    /// （恶魔段在前），走到夜末仍未消费说明收口缺失或数据缺陷——显式记「过时不候」再清空，
    /// 不顺延到下一夜（<see cref="StepMachineFolder"/> 的阶段守卫会在顺延时显式失败）。
    /// </remarks>
    private static void AppendSageNightClose(StepMachineState state, List<GameEvent> events, int toIndex)
    {
        if (state.SageNight is not { } night || toIndex < state.Plan.Slots.Count)
        {
            return;
        }

        if (state.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
        {
            return;
        }

        events.Add(new SageNightClosedEvent
        {
            Note = $"过时不候：贤者（{night.Sage.Value} 号）被恶魔击杀后的展示没有被消费，"
                + "夜晚结束时清空（事实不顺延到下一夜；平台口径见 rulings.md R-0038）",
        });
    }

    /// <summary>进入当前槽位：产出槽位进入事件，并按槽位种类与选择契约派生后续事件。</summary>
    /// <param name="state">步骤机状态（提供当前槽位）。</param>
    /// <param name="ledger">状态账：进入时按**当前**账确认行动者还站得住（见 <see cref="UnavailableReason"/>）。</param>
    /// <param name="seats">本局完整座次；说书人裁定点的实时重建要用（见 <see cref="LivePrompt"/>）。</param>
    /// <param name="prompts">说书人裁定类提示的实时重建来源；null = 走计划快照。</param>
    /// <param name="events">事件出口（就地追加）。</param>
    internal static void Enter(
        StepMachineState state,
        GameState ledger,
        IReadOnlyList<SeatId> seats,
        ISlotPromptSource? prompts,
        List<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(seats);

        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("进入槽位失败：计划已走完");

        events.Add(new SlotEnteredEvent { SlotIndex = state.SlotIndex, SlotId = slot.Id });

        if (slot.Kind == StepSlotKind.Trigger)
        {
            // 触发格：只标记「这一格的时间到了」；是否开交互请求由触发管线按步骤机事实决定
            //（理发师格，R-0033）。不做「空槽位有人却没有契约」的阻塞——这一格的能力属于死亡触发，
            // 不属于格子的持有者本人（本人在场也不行动）。
            return;
        }

        if (slot.Kind != StepSlotKind.Action)
        {
            // 空槽位通常是「角色不在场 / 已死亡」。但角色可能在这一夜被创造出来：有契约的会在结算时
            // 被激活成行动槽位（SlotActivatedEvent）；已经站在场上却仍是空槽位的，说明这一格
            // 在夜晚顺序表上却没有夜间行动契约——**显式阻塞**，与建表期的 plan.contract_missing 同族。
            if (OrphanReason(ledger, slot) is { } orphan)
            {
                events.Add(new SlotBlockedEvent { SlotId = slot.Id, Reason = orphan });
            }

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

        // 进入时按**当前账**再确认一次（D-0013 §1 的配额不受影响）：
        // 说书人在恶魔行动前杀死了尚未唤醒的恶魔，这一格就不再唤醒他；
        // 行动者的角色在夜里被换走，同理。依据见 rulings.md R-0030 第 6 条。
        if (UnavailableReason(ledger, slot) is { } unavailable)
        {
            events.Add(new PromptSkippedEvent { SlotId = slot.Id, Reason = unavailable });
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
                    Reason = $"无合法选项：{slot.Prompt.Context}"
                        + "（按声明的 Skip 走，R-0009；配额照走）",
                });
                break;
            case DecisionPointOutcome.StorytellerDecides:
                var livePrompt = LivePrompt(slot, state.Day?.Days.LastOrDefault(), ledger, seats, prompts, events);
                events.Add(new DecisionPointRaisedEvent
                {
                    SlotId = slot.Id,

                    // 归属 = 这一步的行动者（Action 槽位在进入前已校验 `Actor` 非空）。
                    AttributionSeat = slot.Actor,
                    DecisionPoint = new DecisionPoint
                    {
                        Id = AbilitySettlement.DecisionPointIdOf(state, slot),
                        Prompt = livePrompt ?? slot.Prompt,
                    },
                    SlotPrompt = livePrompt,
                });
                break;
            default:
                events.Add(new SlotBlockedEvent { SlotId = slot.Id, Reason = slot.Prompt.Context });
                break;
        }
    }

    /// <summary>
    /// 说书人裁定点的实时提示：按**当前账**重建，替代计划期的冻结快照。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 计划快照会漏掉**当夜更早槽位**的结果（数学家的失效窗口就是这类上下文），因此入槽时用
    /// <see cref="SettlementContext.SlotPrompts"/> 重建一次。重建账 = 已提交账 + 本批已产出事件
    /// （与手工换角绑定同一姿态：提示按新账构建）。
    /// </para>
    /// <para>
    /// 没有来源、槽位缺行动者 / 归属角色、角色没有契约，或重建结果已不是「说书人裁定点」时返回 null——
    /// 调用方退回计划快照，**本步语义不变**（求值分支仍由快照的求值结果决定）。
    /// </para>
    /// <para>
    /// <paramref name="lastDay"/> 是最近的白天账：回溯型信息能力（卖花女孩 / 城镇公告员）
    /// 的提示要按它推演（R-0037），与建表 / 结算取同一份记录。
    /// </para>
    /// </remarks>
    private static ChoicePrompt? LivePrompt(
        StepSlot slot,
        DayRecord? lastDay,
        GameState ledger,
        IReadOnlyList<SeatId> seats,
        ISlotPromptSource? prompts,
        List<GameEvent> events)
    {
        if (prompts is null || slot.Actor is not { } actor || slot.Owner is not { } abilityOwner)
        {
            return null;
        }

        var live = ledger;
        foreach (var produced in events)
        {
            live = GameStateMachine.Apply(live, produced);
        }

        var rebuilt = prompts.Rebuild(new SlotPromptRequest
        {
            SlotId = slot.Id,
            Character = abilityOwner,
            Actor = actor,
            Seats = seats,
            State = live,
            LastDay = lastDay,
        });

        return rebuilt?.Evaluate() == DecisionPointOutcome.StorytellerDecides ? rebuilt : null;
    }

    /// <summary>
    /// 空槽位却已经有人站在场上的原因；null = 正常的空槽位（角色不在场 / 已死亡 / 非角色槽位）。
    /// </summary>
    /// <remarks>
    /// 只认「恰好一名存活持有者」：多持有是数据损坏，不在这一格的职责里（角色唯一由分配闸与
    /// 角色变更能力自己保证）。
    /// </remarks>
    private static string? OrphanReason(GameState ledger, StepSlot slot)
    {
        if (slot.Character is not { } character)
        {
            return null;
        }

        var holders = ledger.Seats
            .Where(entry => entry.CharacterValue == character && entry.LifeValue == LifeState.Alive)
            .ToArray();
        if (holders.Length != 1)
        {
            return null;
        }

        return $"角色 {character.Value} 此刻由 {holders[0].Seat.Value} 号持有且存活，但这一格没有行动契约"
            + "（夜间行动未实现或未被激活）：拒绝静默跳过（说书人可强推 / 接管 / 重建）";
    }

    /// <summary>
    /// 行动者此刻是否还站得住；返回 null = 可以唤醒。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 账里查不到这一席（内核夹具 / 半初始化场景）时返回 null——**判定不了就不改变行为**，
    /// 与「一次只报本次观测到的维度」是同一副保守姿态。
    /// </para>
    /// <para>
    /// 两条扩展：① 代行格要求行动者身上那份**授予**还在（哲学家二次获得替换旧授予后，
    /// 旧代行格不得再开请求，R-0036 / R-0053）；② 死者只在「重获能力」窗口生效、且这一格
    /// 就是他本人的能力格时放行（集骨者，R-0054）。
    /// </para>
    /// </remarks>
    internal static string? UnavailableReason(GameState ledger, StepSlot slot)
    {
        var actor = slot.Actor!.Value;
        var entry = ledger.Seat(actor);
        if (entry is null)
        {
            return null;
        }

        if (slot.Owner is { } owner
            && slot.Character is { } actorCharacter
            && owner != actorCharacter
            && !ledger.HasLiveGrantOf(actor, owner))
        {
            return $"这一格代行的是 {owner.Value} 的能力，但行动者（{actor.Value} 号）身上已经没有这份授予："
                + "本步跳过（授予已被替换 / 终止；R-0036 / R-0053）";
        }

        if (entry.LifeValue == LifeState.Dead)
        {
            var regained = slot.Owner is { } slotOwner
                && slotOwner == entry.CharacterValue
                && ledger.AbilityPresentOn(actor) == true;
            if (!regained)
            {
                return $"行动者（{actor.Value} 号）已经死亡：本步不唤醒（配额照走；rulings.md R-0030）";
            }
        }

        if (entry.CharacterValue is { } current && slot.Character is { } expected && current != expected)
        {
            return $"行动者（{actor.Value} 号）现在的角色是 {current.Value}，不是 {expected.Value}："
                + "本步跳过（配额照走；过时不候）";
        }

        return null;
    }

    /// <summary>为一个行动槽位构造操作请求（槽位来源：随槽位推进了结、消耗夜晚配额）。</summary>
    private static OperationRequest BuildRequest(StepMachineState state, StepSlot slot) =>
        new()
        {
            Id = new OperationRequestId(SlotKeyOf(state, slot)),
            Addressee = slot.Actor!.Value,
            Origin = OperationRequestOrigin.ForSlot(slot.Id, state.Plan.Label, state.SlotIndex),
            Prompt = slot.Prompt!,
            Dependencies = slot.Dependencies,
        };

    /// <summary>
    /// 槽位的稳定键：首遍 <c>{计划标签}:{槽位标识}</c>，重进遍次（大于 1）追加 <c>#{遍次}</c>。
    /// </summary>
    /// <remarks>
    /// 操作请求标识、裁定点标识与契约派生的效果标识都从它派生——同一槽位的两次结算各自唯一，
    /// 否则第二次会被折叠层按"重复标识"显式拒绝（R-0052 第 2 条）。
    /// </remarks>
    internal static string SlotKeyOf(StepMachineState state, StepSlot slot) =>
        state.SlotPass <= 1
            ? $"{state.Plan.Label}:{slot.Id}"
            : $"{state.Plan.Label}:{slot.Id}#{state.SlotPass}";
}
