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

    /// <summary>产出一条自动推进事件；推进后进入新槽位（计划走完则补阶段完成事件）。</summary>
    internal static void AppendAdvance(StepMachineState state, GameState ledger, List<GameEvent> events)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        AppendPitHagNightClose(state, ledger, events);
        AppendBarberNightClose(state, events, to);

        events.Add(new SlotAdvancedEvent { FromIndex = from, ToIndex = to });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            ledger,
            events);
    }

    /// <summary>产出一条强推事件（说书人兜底，D-0014）；推进后进入新槽位。</summary>
    internal static void AppendForceAdvance(
        StepMachineState state,
        GameState ledger,
        List<GameEvent> events,
        string reason)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        AppendPitHagNightClose(state, ledger, events);
        AppendBarberNightClose(state, events, to);

        events.Add(new SlotForceAdvancedEvent { FromIndex = from, ToIndex = to, Reason = reason });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            ledger,
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

    /// <summary>进入当前槽位：产出槽位进入事件，并按槽位种类与选择契约派生后续事件。</summary>
    /// <param name="state">步骤机状态（提供当前槽位）。</param>
    /// <param name="ledger">状态账：进入时按**当前**账确认行动者还站得住（见 <see cref="UnavailableReason"/>）。</param>
    /// <param name="events">事件出口（就地追加）。</param>
    internal static void Enter(StepMachineState state, GameState ledger, List<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(ledger);

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
    /// 账里查不到这一席（内核夹具 / 半初始化场景）时返回 null——**判定不了就不改变行为**，
    /// 与「一次只报本次观测到的维度」是同一副保守姿态。
    /// </remarks>
    private static string? UnavailableReason(GameState ledger, StepSlot slot)
    {
        var entry = ledger.Seat(slot.Actor!.Value);
        if (entry is null)
        {
            return null;
        }

        if (entry.LifeValue == LifeState.Dead)
        {
            return $"行动者（{slot.Actor.Value} 号）已经死亡：本步不唤醒（配额照走；rulings.md R-0030）";
        }

        if (entry.CharacterValue is { } current && slot.Character is { } expected && current != expected)
        {
            return $"行动者（{slot.Actor.Value} 号）现在的角色是 {current.Value}，不是 {expected.Value}："
                + "本步跳过（配额照走；过时不候）";
        }

        return null;
    }

    /// <summary>为一个行动槽位构造操作请求（槽位来源：随槽位推进了结、消耗夜晚配额）。</summary>
    private static OperationRequest BuildRequest(StepMachineState state, StepSlot slot) =>
        new()
        {
            Id = new OperationRequestId($"{state.Plan.Label}:{slot.Id}"),
            Addressee = slot.Actor!.Value,
            Origin = OperationRequestOrigin.ForSlot(slot.Id, state.Plan.Label, state.SlotIndex),
            Prompt = slot.Prompt!,
            Dependencies = slot.Dependencies,
        };
}
