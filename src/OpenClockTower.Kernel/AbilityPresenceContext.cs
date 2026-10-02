namespace OpenClockTower.Kernel;

/// <summary>能力存续判定的输入：当前账 + 座次。</summary>
/// <remarks>
/// 「存活人数 ≤3」这类条件必须同时读账（每席生死）与座次（谁算在内），因此两样一起给；
/// 座次按座位号升序 = 圆桌顺序。
/// </remarks>
public sealed record AbilityPresenceContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局完整座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }
}
