using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人视图：完整看板——阶段、当前槽位、控制模式、卡点、待裁定的裁定点、阻塞原因。
/// </summary>
/// <remarks>
/// 说书人是这台步骤机的最终裁量者（D-0014）：这里给足信息，兜底入口才有意义。
/// </remarks>
public sealed record StorytellerView
{
    /// <summary>视图对应的事件流序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>当前阶段；未开局为 null。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>当前控制模式；未开局为 null。</summary>
    public ControlMode? Control { get; init; }

    /// <summary>当前槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>本计划的槽位总数。</summary>
    public required int SlotCount { get; init; }

    /// <summary>当前槽位标识；计划已走完为 null。</summary>
    public StepSlotId? CurrentSlotId { get; init; }

    /// <summary>本计划是否已走完。</summary>
    public required bool PlanCompleted { get; init; }

    /// <summary>卡点摘要；没有挂起请求时为 null。</summary>
    public PendingRequestSummary? Pending { get; init; }

    /// <summary>等待说书人裁定的裁定点（R-0009）；没有时为 null。</summary>
    public DecisionPoint? AwaitingDecision { get; init; }

    /// <summary>阻塞原因（R-0009 BlockAndAlert）；没有阻塞时为 null。</summary>
    public string? BlockedReason { get; init; }

    /// <summary>当前槽位的行动者（上帝视角）；空槽位 / 已走完为 null。</summary>
    public SeatId? CurrentSlotActor { get; init; }

    /// <summary>当前槽位请求的上下文（为什么要做这个选择）。</summary>
    public string? CurrentSlotContext { get; init; }

    /// <summary>最近的状态变化（含原因与归因，最新在后）。</summary>
    public required IReadOnlyList<SeatChangeSnapshot> RecentSeatChanges { get; init; }
}
