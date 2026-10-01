namespace OpenClockTower.Contracts;

/// <summary>说书人视图：完整看板 + 兜底所需的一切（D-0014）。</summary>
public sealed record StorytellerViewDto
{
    /// <summary>视图对应的最新序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>当前大阶段。</summary>
    public required string Phase { get; init; }

    /// <summary>控制模式：Automatic / StorytellerTakeover。</summary>
    public required string Control { get; init; }

    /// <summary>当前槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>槽位总数。</summary>
    public required int SlotCount { get; init; }

    /// <summary>当前槽位标识；计划已走完为 null。</summary>
    public string? CurrentSlotId { get; init; }

    /// <summary>本计划是否已走完。</summary>
    public required bool PlanCompleted { get; init; }

    /// <summary>卡点摘要。</summary>
    public PendingRequestDto? Pending { get; init; }

    /// <summary>等待说书人裁定的裁定点标识；没有时为 null。</summary>
    public string? AwaitingDecisionId { get; init; }

    /// <summary>阻塞原因；没有阻塞时为 null。</summary>
    public string? BlockedReason { get; init; }

    /// <summary>当前槽位的行动者（上帝视角）；空槽位 / 已走完为 null。</summary>
    public int? CurrentSlotActor { get; init; }

    /// <summary>当前槽位请求的上下文。</summary>
    public string? CurrentSlotContext { get; init; }

    /// <summary>最近的状态变化（含原因与归因，最新在后）。</summary>
    public required SeatChangeDto[] RecentSeatChanges { get; init; }
}
