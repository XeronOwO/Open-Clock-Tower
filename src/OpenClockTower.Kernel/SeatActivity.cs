namespace OpenClockTower.Kernel;

/// <summary>「近期活动账」里的一条活动：谁、发生了什么、为什么（原因原样来自事件）。</summary>
public sealed record SeatActivity
{
    /// <summary>活动分类。</summary>
    public required SeatActivityKind Kind { get; init; }

    /// <summary>涉及的席位（角色 / 阵营变化与死亡都是这个席位本人）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>事件里记的原因（如「被恶魔击杀」）；null = 事件没给原因。</summary>
    public string? Reason { get; init; }
}
