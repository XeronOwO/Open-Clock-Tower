namespace OpenClockTower.Contracts;

/// <summary>
/// 说书人视角的一条座位状态变化：谁因为什么原因变成了什么样（上帝视角）。
/// </summary>
public sealed record SeatChangeDto
{
    /// <summary>发生变化的座位。</summary>
    public required int Seat { get; init; }

    /// <summary>变化后的生死；null = 未观测。</summary>
    public string? Life { get; init; }

    /// <summary>变化后的角色；null = 未观测。</summary>
    public string? Character { get; init; }

    /// <summary>变化原因。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方；无人导致时为 null。</summary>
    public int? CausedBy { get; init; }

    /// <summary>对应事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>记录时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
