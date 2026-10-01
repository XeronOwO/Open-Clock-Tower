namespace OpenClockTower.Kernel;

/// <summary>常驻效果求值的输入：当前账 + 座次。</summary>
/// <remarks>
/// 座次（<see cref="Seats"/>）按座位号升序 = 圆桌顺序；规则层据此算「邻近 / 顺时针」这类关系。
/// </remarks>
public sealed record StandingEffectContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局完整座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }
}
