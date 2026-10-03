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
    /// <param name="state">状态账（白天权限判定要读生死）。</param>
    /// <param name="seats">本局完整座次（算可提名目标用）。</param>
    /// <param name="sequence">投影对应的事件序号。</param>
    /// <param name="seat">接收者席位。</param>
    /// <param name="trackers">会话派生跟踪器：发给该席位的信息结果与公开生死面（R-0022）。</param>
    public static PlayerView ForSeat(
        StepMachineState? machine,
        GameState state,
        IReadOnlyList<SeatId> seats,
        long sequence,
        SeatId seat,
        SessionTrackers trackers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(trackers);

        var pending = machine?.PendingRequest;
        var ended = machine?.Outcome is not null;

        // 结束态不再下发任何请求：终局快照可能还留着最后一条请求（作废要重排结束批次的管线），
        // 而它对玩家已经答不了（一切提交都被 phase.game_ended 拒）——推给他就是一条死信。
        var deliverable = !ended && pending is { Status: OperationRequestStatus.Pending } && pending.Addressee == seat
            ? pending
            : null;

        return new PlayerView
        {
            Seat = seat,
            Phase = machine?.Plan.Phase,
            PendingRequest = deliverable,
            InformationResults = trackers.InformationResultsFor(seat),
            Day = DayProjection.ForSeat(machine?.Day, state, seats, seat, trackers.PublicLife),
            Outcome = machine?.Outcome,
            KlutzChoices = [.. (machine?.KlutzChoices ?? []).Select(PublicKlutzChoice)],
            Sequence = sequence,
        };
    }

    /// <summary>
    /// 玩家面的呆瓜选择记录：**只公开"选了什么"**。跳过记录的原因写的是能力为何没生效
    /// （醉酒 / 中毒 / 被谁作废）——那是说书人专属维度，按 R-0012 与 D-0012 §4.3 不下发；
    /// 说书人视图保留完整 <see cref="KlutzChoiceRecord.Detail"/>。
    /// </summary>
    private static KlutzChoiceRecord PublicKlutzChoice(KlutzChoiceRecord record) =>
        record.IsMade ? record : record with { Detail = SkippedChoiceDetail };

    /// <summary>跳过记录的公开文案：只说"没做出选择"，不解释为什么（原因只在说书人视图）。</summary>
    private const string SkippedChoiceDetail = "呆瓜本次没有做出选择";

    /// <summary>说书人视图（含卡点时长、状态账、效果归因、能力结算结论、每步摘要与房间健康位；时长由应用层时钟算出）。</summary>
    public static StorytellerView ForStoryteller(
        StepMachineState? machine,
        GameState state,
        RoomHealth health,
        long sequence,
        DateTimeOffset? pendingSince,
        DateTimeOffset now,
        IReadOnlyList<SeatChangeSnapshot> recentSeatChanges,
        IReadOnlyList<SeatAnnotation> annotations,
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
                SlotId = request.Origin.SlotId,
                SlotIndex = request.Origin.SlotIndex,
                TriggerReason = request.Origin.TriggerReason,
                Waiting = pendingSince is { } since ? now - since : null,
            };
        }

        return new StorytellerView
        {
            Sequence = sequence,
            Phase = machine?.Plan.Phase,
            Control = machine?.Control,
            Health = health,
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
            Day = machine?.Day?.Days.LastOrDefault(),
            Outcome = machine?.Outcome,
            KlutzChoices = machine?.KlutzChoices ?? [],

            // 麻脸巫婆之夜的死亡裁量窗口（R-0030）：说书人要据此裁定待定死亡、
            // 并在窗口内追加死亡——玩家投影里没有它（D-0012 §4.3）。
            PitHagNight = machine?.PitHagNight,

            // 说书人注记（D-0019）：自由文本提示标记只说书人可见；玩家投影里没有这条字段。
            Annotations = annotations,
        };
    }
}
