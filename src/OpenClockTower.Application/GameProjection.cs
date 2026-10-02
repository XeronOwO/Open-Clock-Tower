using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 投影：把步骤机状态折算成"某个受众该看到什么"。
/// </summary>
/// <remarks>
/// 依据 D-0012 §4.3：越权信息**根本不下发**——玩家投影里没有槽位、进度、他人活动（D-0013 §5）。
/// </remarks>
public static class GameProjection
{
    /// <summary>某个玩家的投影。</summary>
    /// <param name="machine">步骤机状态。</param>
    /// <param name="sequence">投影对应的事件序号。</param>
    /// <param name="seat">接收者席位。</param>
    /// <param name="informationResults">派生自事件流、发给该席位的信息结果。</param>
    public static PlayerView ForSeat(
        StepMachineState? machine,
        long sequence,
        SeatId seat,
        IReadOnlyList<InformationResultSnapshot> informationResults)
    {
        ArgumentNullException.ThrowIfNull(informationResults);

        var pending = machine?.PendingRequest;
        var deliverable = pending is { Status: OperationRequestStatus.Pending } && pending.Addressee == seat
            ? pending
            : null;

        return new PlayerView
        {
            Seat = seat,
            Phase = machine?.Plan.Phase,
            PendingRequest = deliverable,
            InformationResults = informationResults,
            Sequence = sequence,
        };
    }

    /// <summary>说书人视图（含卡点时长、状态账、效果归因、能力结算结论与每步摘要；时长由应用层时钟算出）。</summary>
    public static StorytellerView ForStoryteller(
        StepMachineState? machine,
        GameState state,
        long sequence,
        DateTimeOffset? pendingSince,
        DateTimeOffset now,
        IReadOnlyList<SeatChangeSnapshot> recentSeatChanges,
        AbilityResolutionSnapshot? lastResolution = null,
        StepDigest? stepDigest = null,
        VoidedRequestSnapshot? lastVoidedRequest = null)
    {
        PendingRequestSummary? pendingSummary = null;
        if (machine?.PendingRequest is { Status: OperationRequestStatus.Pending } request)
        {
            pendingSummary = new PendingRequestSummary
            {
                Seat = request.Addressee,
                RequestId = request.Id,
                SlotId = request.SlotId,
                SlotIndex = request.IssuedAtSlotIndex,
                Waiting = pendingSince is { } since ? now - since : null,
            };
        }

        return new StorytellerView
        {
            Sequence = sequence,
            Phase = machine?.Plan.Phase,
            Control = machine?.Control,
            SlotIndex = machine?.SlotIndex ?? 0,
            SlotCount = machine?.Plan.Slots.Count ?? 0,
            CurrentSlotId = machine?.CurrentSlot?.Id,
            PlanCompleted = machine?.IsPlanCompleted ?? false,
            Pending = pendingSummary,
            AwaitingDecision = machine?.AwaitingDecision,
            BlockedReason = machine?.Block?.Reason,
            CurrentSlotActor = machine?.CurrentSlot?.Actor,
            CurrentSlotContext = machine?.CurrentSlot?.Prompt?.Context,
            RecentSeatChanges = recentSeatChanges,
            Seats = state.Seats,
            PersistentEffects = state.PersistentEffects,
            InstantaneousEffects = state.InstantaneousEffects,
            AbilityUses = state.AbilityUses.Entries,
            Malfunctions = state.Malfunctions.Entries,
            LastResolution = lastResolution,
            StepDigest = stepDigest,
            LastVoidedRequest = lastVoidedRequest,
        };
    }
}
