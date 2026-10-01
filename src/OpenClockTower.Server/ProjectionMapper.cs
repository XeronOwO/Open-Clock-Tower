using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>Application / Kernel → Contracts 的翻译：wire 形状只在这一层出现。</summary>
public static class ProjectionMapper
{
    /// <summary>命令结果 → DTO。</summary>
    public static CommandResultDto ToDto(CommandResult result) => new()
    {
        Kind = result.Kind.ToString(),
        Sequence = result.Sequence,
        RejectionCode = result.Rejection?.Code,
        RejectionMessage = result.Rejection?.Message,
        Failure = result.Failure,
        MachineEquivalent = result.Rebuild?.MachineEquivalent,
        SnapshotEquivalent = result.Rebuild?.SnapshotEquivalent,
    };

    /// <summary>操作请求 → DTO（刻意不带槽位 / 轮次 / 进度）。</summary>
    public static OperationRequestDto ToDto(OperationRequest request) => new()
    {
        RequestId = request.Id.Value,
        Seat = request.Addressee.Value,
        Context = request.Prompt.Context,
        Options = request.Prompt.Options
            .Select(option => new DecisionOptionDto { Value = option.Value, Preview = option.Preview })
            .ToArray(),
    };

    /// <summary>作废内容 → DTO。</summary>
    public static OperationRequestVoidedDto ToDto(OperationRequestId requestId, OperationRequestVoid voided) => new()
    {
        RequestId = requestId.Value,
        Reason = voided.Reason.ToString(),
        Note = voided.Note,
    };

    /// <summary>玩家视图 → DTO。</summary>
    public static PlayerViewDto ToDto(PlayerView view) => new()
    {
        Seat = view.Seat.Value,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        PendingRequest = view.PendingRequest is { } pending ? ToDto(pending) : null,
    };

    /// <summary>重连包 → DTO。</summary>
    public static ReconnectBundleDto ToDto(ReconnectBundle bundle) => new()
    {
        Sequence = bundle.Sequence,
        View = ToDto(bundle.View),
        Events = bundle.EventsSince.Select(ToDto).ToArray(),
    };

    /// <summary>玩家可见事件 → DTO。</summary>
    public static PlayerEventDto ToDto(PlayerEvent playerEvent) => new()
    {
        Sequence = playerEvent.Sequence,
        Kind = playerEvent.Kind.ToString(),
        Phase = playerEvent.Phase?.ToString(),
        Request = playerEvent.Request is { } request ? ToDto(request) : null,
        RequestId = playerEvent.RequestId?.Value,
        OptionValue = playerEvent.OptionValue,
        VoidReason = playerEvent.Void?.Reason.ToString(),
        VoidNote = playerEvent.Void?.Note,
    };

    /// <summary>说书人视图 → DTO。</summary>
    public static StorytellerViewDto ToDto(StorytellerView view) => new()
    {
        Sequence = view.Sequence,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        Control = view.Control?.ToString() ?? "NotStarted",
        SlotIndex = view.SlotIndex,
        SlotCount = view.SlotCount,
        CurrentSlotId = view.CurrentSlotId?.Value,
        PlanCompleted = view.PlanCompleted,
        Pending = view.Pending is { } pending
            ? new PendingRequestDto
            {
                Seat = pending.Seat.Value,
                RequestId = pending.RequestId.Value,
                SlotId = pending.SlotId.Value,
                SlotIndex = pending.SlotIndex,
                WaitingSeconds = pending.Waiting?.TotalSeconds,
            }
            : null,
        AwaitingDecisionId = view.AwaitingDecision?.Id.Value,
        BlockedReason = view.BlockedReason,
        CurrentSlotActor = view.CurrentSlotActor?.Value,
        CurrentSlotContext = view.CurrentSlotContext,
        RecentSeatChanges = view.RecentSeatChanges.Select(ToDto).ToArray(),
    };

    /// <summary>状态变化记录 → DTO。</summary>
    public static SeatChangeDto ToDto(SeatChangeSnapshot change) => new()
    {
        Seat = change.Seat.Value,
        Life = change.Life?.ToString(),
        Character = change.Character?.ToString(),
        Reason = change.Reason,
        CausedBy = change.CausedBy?.Value,
        Sequence = change.Sequence,
        RecordedAt = change.RecordedAt,
    };
}
