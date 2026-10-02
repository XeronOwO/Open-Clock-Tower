namespace OpenClockTower.Kernel;

/// <summary>
/// 把事件流折叠回步骤机状态——重放、重启恢复、撤销的共同基础。
/// </summary>
/// <remarks>
/// 从 <see cref="StepMachine"/> 拆出：`Handle` 负责"产事件"，这里负责"把事件折回状态"。
/// 顺序损坏一律显式抛错（D-0014 能力 3）。账事件（座位状态 / 效果 / 疯狂要求）不创建状态：
/// 它们可以先于任何阶段出现（开局分配），此时步骤机保持"尚未开始"（null）。
/// </remarks>
internal static class StepMachineFolder
{
    /// <summary>把一条事件折叠回状态。</summary>
    internal static StepMachineState? Apply(StepMachineState? state, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            PhaseStartedEvent started => new StepMachineState
            {
                Plan = started.Plan,
                SlotIndex = 0,
                Quota = SlotQuotaState.Running,
                Control = started.Control,

                // 白天账跨阶段保留：死亡玩家的「死后仅一次投票」与逐日事实（卖花女孩 / 城镇公告员要读）
                // 不随夜晚开始清零。
                Day = state?.Day,
            },
            SlotEnteredEvent entered => Require(state, entered) with
            {
                SlotIndex = entered.SlotIndex,
                Quota = SlotQuotaState.Running,
                PendingRequest = null,
                AwaitingDecision = null,
                Block = null,
            },
            OperationRequestIssuedEvent issued => Require(state, issued) with
            {
                PendingRequest = issued.Request,
            },
            OperationRequestAnsweredEvent answered => Require(state, answered) with
            {
                PendingRequest = RequireOpenPending(state, answered.RequestId) with
                {
                    Status = OperationRequestStatus.Answered,
                    Answer = answered.Answer,
                },
            },
            OperationRequestVoidedEvent voided => Require(state, voided) with
            {
                PendingRequest = RequireOpenPending(state, voided.RequestId) with
                {
                    Status = OperationRequestStatus.Voided,
                    Voided = voided.Void,
                },
            },
            SlotQuotaElapsedEvent elapsed => Require(state, elapsed) with
            {
                Quota = SlotQuotaState.Elapsed,
            },
            SlotAdvancedEvent advanced => ApplyAdvance(state, advanced.FromIndex, advanced.ToIndex),
            SlotForceAdvancedEvent forceAdvanced => ApplyAdvance(state, forceAdvanced.FromIndex, forceAdvanced.ToIndex),
            PhaseCompletedEvent completed => Require(state, completed),
            ControlModeChangedEvent control => Require(state, control) with { Control = control.Mode },
            PromptSkippedEvent skipped => Require(state, skipped),
            SeatStateChangedEvent => state,
            DecisionPointRaisedEvent raised => Require(state, raised) with
            {
                AwaitingDecision = raised.DecisionPoint,
            },
            DecisionPointResolvedEvent resolved => ResolveDecision(state, resolved),
            SlotBlockedEvent blocked => Require(state, blocked) with
            {
                Block = new StepBlock { Reason = blocked.Reason },
            },

            // 白天事件：折叠白天账（提名 / 投票 / 计票 / 处决 / 结束）；槽位推进由 CloseDay / 强推产出的事件驱动。
            DayStartedEvent => ApplyDay(state, gameEvent),
            NominationMadeEvent => ApplyDay(state, gameEvent),
            VoteCastEvent => ApplyDay(state, gameEvent),
            VoteCountedEvent => ApplyDay(state, gameEvent),
            ExecutedEvent => ApplyDay(state, gameEvent),
            DayClosedEvent => ApplyDay(state, gameEvent),

            // 状态账的事件：进同一条事件流，但步骤机状态不由它们改变
            // （座位状态变化对步骤机的影响是"作废依赖失效的挂起请求"，在 Handle 阶段已经处理完）。
            // 它们可以**先于任何阶段**出现（开局分配与初始状态）——此时步骤机保持"尚未开始"（null）：
            // 账是同一事件流上的独立派生视图（D-0015），不允许账事件把 null 变成"已开始"。
            PersistentEffectAppliedEvent => state,
            PersistentEffectTerminatedEvent => state,
            InstantaneousEffectAppliedEvent => state,
            MadnessRequirementIssuedEvent => state,
            AbilityResolvedEvent => state,
            InformationResultIssuedEvent => state,

            _ => throw new InvalidOperationException($"未知事件类型：{gameEvent.GetType().Name}"),
        };
    }

    /// <summary>按序折叠一批事件；空批（或只含账事件）时结果为 null。</summary>
    internal static StepMachineState? ApplyAll(StepMachineState? state, IEnumerable<GameEvent> events)
    {
        var current = state;
        foreach (var gameEvent in events)
        {
            current = Apply(current, gameEvent);
        }

        return current;
    }

    private static StepMachineState Require(StepMachineState? state, GameEvent gameEvent) =>
        state ?? throw new InvalidOperationException($"事件流顺序损坏：{gameEvent.GetType().Name} 之前没有状态");

    private static OperationRequest RequireOpenPending(StepMachineState? state, OperationRequestId requestId)
    {
        var pending = state?.PendingRequest;
        if (pending is null || pending.Id != requestId)
        {
            throw new InvalidOperationException($"事件流顺序损坏：{requestId} 不是当前挂起的请求");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            throw new InvalidOperationException($"事件流顺序损坏：请求 {requestId} 已经了结，不能再次了结");
        }

        return pending;
    }

    private static StepMachineState ResolveDecision(StepMachineState? state, DecisionPointResolvedEvent resolved)
    {
        var current = Require(state, resolved);
        var decision = current.AwaitingDecision;
        if (decision is null || decision.Id != resolved.DecisionPointId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{resolved.DecisionPointId} 不是当前挂起的裁定点");
        }

        return current with { AwaitingDecision = null };
    }

    private static StepMachineState ApplyAdvance(StepMachineState? state, int fromIndex, int toIndex)
    {
        var current = state
            ?? throw new InvalidOperationException("事件流顺序损坏：推进事件之前没有状态");

        if (fromIndex != current.SlotIndex)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：推进起点 {fromIndex} 与当前槽位 {current.SlotIndex} 不一致");
        }

        if (toIndex < fromIndex || toIndex > current.Plan.Slots.Count)
        {
            throw new InvalidOperationException($"事件流顺序损坏：推进目标 {toIndex} 越界");
        }

        var next = current with
        {
            SlotIndex = toIndex,
            Quota = SlotQuotaState.Running,
            PendingRequest = null,
            AwaitingDecision = null,
            Block = null,
        };

        // 白天计划走完 = 白天结束：必须先有 DayClosedEvent（CloseDay 或强推兜底产出），
        // 否则事件流顺序损坏——恢复必须显式失败，不允许"阶段结束了、白天账还开着"。
        if (toIndex >= current.Plan.Slots.Count
            && current.Plan.Phase == GamePhase.Day
            && next.Day?.OpenDay is { } openDay)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {openDay.DayNumber} 的计划走完，却没有结束白天的事件");
        }

        return next;
    }

    /// <summary>把一条白天事件折进白天账（状态本身由 Require 保证已开始）。</summary>
    private static StepMachineState ApplyDay(StepMachineState? state, GameEvent gameEvent)
    {
        var current = Require(state, gameEvent);
        return current with { Day = DayLedgerFolder.Apply(current.Day, gameEvent) };
    }
}
