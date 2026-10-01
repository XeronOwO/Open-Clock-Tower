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

    /// <summary>变化后的阵营；null = 未观测。</summary>
    public string? Alignment { get; init; }

    /// <summary>变化后的醉酒状态；null = 未观测。</summary>
    public string? Drunk { get; init; }

    /// <summary>变化后的中毒状态；null = 未观测。</summary>
    public string? Poison { get; init; }

    /// <summary>变化原因。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方；无人导致时为 null。</summary>
    public int? CausedBy { get; init; }

    /// <summary>本次变化由哪条持续型效果导致；null = 与效果无关（开局分配 / 说书人上报等）。</summary>
    public string? EffectId { get; init; }

    /// <summary>对应事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>记录时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
