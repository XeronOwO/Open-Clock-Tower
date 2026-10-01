using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人的"卡点"摘要：谁在卡着、卡在哪一步、卡了多久（D-0011 代价条款）。
/// </summary>
public sealed record PendingRequestSummary
{
    /// <summary>被卡住的玩家。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>挂起的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>卡在哪个槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>计划里的第几个槽位。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>已等待时长；时间线未知时为 null（不编造）。</summary>
    public required TimeSpan? Waiting { get; init; }
}
