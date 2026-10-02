namespace OpenClockTower.Kernel;

/// <summary>公开生死面上的一条事实：某个席位对外可见的生死状态。</summary>
/// <remarks>
/// 同一个形状同时表达两种投影：牌面上的"现在是什么"与公告里的"变成了什么"
/// （死亡 = <see cref="LifeState.Dead"/>，复活 = <see cref="LifeState.Alive"/>）。
/// 依据 <c>docs/standard/rulings.md</c> R-0022：公告不含死因，因此本类型没有原因 / 归因字段。
/// </remarks>
public sealed record PublicLifeEntry
{
    /// <summary>席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>对外可见的生死。</summary>
    public required LifeState State { get; init; }
}
