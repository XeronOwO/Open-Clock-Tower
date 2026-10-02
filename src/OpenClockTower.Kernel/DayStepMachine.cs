namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机里的白天输入处理：开白天、提名 / 投票 / 计票 / 结束白天的槽位推进、配额与强推兜底。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 拆出：白天规则的判定在 <see cref="DayMachine"/>，这里只负责
/// "把白天结果接回步骤机"（阶段前置检查、槽位推进事件、拒绝码翻译）。拆分的直接原因是
/// 单文件 600 行门禁——一个类同时装下昼夜两套推进，本来就说明职责不止一份。
/// </para>
/// <para>
/// 白天计划只有一个 <see cref="StepSlotKind.DayWindow"/> 槽位、不消耗配额：结束白天 = 走完计划；
/// 强推 = 立即关账、不产生处决（D-0014 能力 1）。
/// </para>
/// </remarks>
internal static class DayStepMachine
{
    /// <summary>开启白天：校验计划形状与天数，产出阶段 / 白天账 / 槽位事件。</summary>
    internal static StepMachineOutcome StartDay(
        StepPlan plan,
        int dayNumber,
        StepMachineState? previous,
        ControlMode control)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Phase != GamePhase.Day)
        {
            throw new ArgumentException($"白天计划必须是 Day 阶段，实际是 {plan.Phase}", nameof(plan));
        }

        if (plan.Slots.Count != 1 || plan.Slots[0].Kind != StepSlotKind.DayWindow)
        {
            throw new ArgumentException("白天计划必须恰好包含一个 DayWindow 槽位", nameof(plan));
        }

        if (dayNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dayNumber), dayNumber, "白天序号必须从 1 开始");
        }

        var events = new List<GameEvent>(capacity: 4)
        {
            new PhaseStartedEvent { Plan = plan, Control = control },
            new DayStartedEvent { DayNumber = dayNumber },
            new SlotEnteredEvent { SlotIndex = 0, SlotId = plan.Slots[0].Id },
        };

        return new StepMachineOutcome
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(previous, events)
                ?? throw new InvalidOperationException("事件流损坏：开启白天没有产出步骤机状态"),
            Events = events,
        };
    }

    /// <summary>处理一条白天输入（调用方已保证输入类型属于白天四类之一）。</summary>
    internal static StepMachineOutcome Handle(
        StepMachineState state,
        SettlementContext context,
        StepMachineInput input)
    {
        if (state.Plan.Phase != GamePhase.Day)
        {
            return Reject(state, "day.not_open", "当前阶段不是白天");
        }

        var day = state.Day ?? DayState.Empty;
        var outcome = input switch
        {
            NominateInput nominate => DayMachine.Nominate(day, context, nominate),
            CastVoteInput castVote => DayMachine.CastVote(day, context, castVote),
            CountVotesInput countVotes => DayMachine.CountVotes(day, context, countVotes),
            CloseDayInput => DayMachine.CloseDay(day, context),
            _ => throw new InvalidOperationException($"不是白天输入：{input.GetType().Name}"),
        };

        if (outcome.IsRejected)
        {
            return Reject(state, outcome.RejectionCode!, outcome.RejectionNote!);
        }

        if (input is CloseDayInput)
        {
            // 结束白天 = 走完白天计划（不是强推）：先落白天账，再推槽位、收计划。
            var closeEvents = new List<GameEvent>(outcome.Events);
            var after = StepMachineFolder.ApplyAll(state, closeEvents)
                ?? throw new InvalidOperationException("事件流损坏：结束白天后丢失步骤机状态");
            closeEvents.Add(new SlotAdvancedEvent { FromIndex = after.SlotIndex, ToIndex = after.SlotIndex + 1 });
            closeEvents.Add(new PhaseCompletedEvent { PlanLabel = after.Plan.Label });
            return Applied(state, closeEvents);
        }

        return Applied(state, [.. outcome.Events]);
    }

    /// <summary>白天窗口不消耗配额：收到配额输入也不产出事件、不推进。</summary>
    internal static StepMachineOutcome QuotaElapsed(StepMachineState state) => Applied(state, []);

    /// <summary>强推白天：立即结束白天（不产生处决）并走完计划；没有白天账、或还有未计票的提名时显式拒绝。</summary>
    internal static StepMachineOutcome ForceAdvance(StepMachineState state, ForceAdvanceInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, "day.plan_completed", "本计划已走完");
        }

        if (state.Day?.OpenDay is not { } openDay)
        {
            return Reject(state, "day.not_open", "白天账缺失或已关闭：请先显式重建房间，再继续");
        }

        if (openDay.OpenNomination is not null)
        {
            // 强推只跳过"说书人还没决定何时结束"，不替他说出计票结论：
            // 否则白天账里会留下一条永远停在 Voting、票权也没结算的提名（对抗性复核 F-2）。
            // 出路仍然永远存在：先计票（计票结论本就由说书人掌握），再结束白天。
            return Reject(
                state,
                "day.nomination_not_counted",
                "还有提名没有计票：先计票再结束白天（强推不替说书人拍板计票结论）");
        }

        var events = new List<GameEvent>
        {
            new DayClosedEvent { DayNumber = openDay.DayNumber },
        };
        var after = StepMachineFolder.ApplyAll(state, events)
            ?? throw new InvalidOperationException("事件流损坏：强推白天后丢失步骤机状态");

        events.Add(new SlotForceAdvancedEvent
        {
            FromIndex = after.SlotIndex,
            ToIndex = after.SlotIndex + 1,
            Reason = input.Reason,
        });
        events.Add(new PhaseCompletedEvent { PlanLabel = after.Plan.Label });
        return Applied(state, events);
    }

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理白天输入后丢失步骤机状态"),
            Events = events,
        };

    private static StepMachineOutcome Reject(StepMachineState state, string code, string note) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Rejected,
            State = state,
            Events = [],
            RejectionCode = code,
            RejectionNote = note,
        };
}
