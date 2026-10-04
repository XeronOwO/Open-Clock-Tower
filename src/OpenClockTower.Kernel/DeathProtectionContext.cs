namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护查询的一次输入：账 + 在局座次 + 当天白天账 + 被问的席位与死因。
/// </summary>
/// <remarks>
/// 白天账随上下文传入，让规则层来源能读到当天作用域的裁定（怪咖「今天是否有趣」，R-0048）；
/// 契约只读，不写账、不产事件。
/// </remarks>
public sealed record DeathProtectionContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局在局座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>被问的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>本次死亡的死因。</summary>
    public required DeathProtectionCause Cause { get; init; }

    /// <summary>当前进行中的白天账；没有白天时为 null。</summary>
    public DayRecord? Day { get; init; }
}
