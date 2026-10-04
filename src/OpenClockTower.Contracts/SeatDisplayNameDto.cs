namespace OpenClockTower.Contracts;

/// <summary>席位 → 玩家名（D-0021）：公开呈现信息，同桌所有人收到同一份。</summary>
public sealed record SeatDisplayNameDto
{
    /// <summary>席位号。</summary>
    public required int Seat { get; init; }

    /// <summary>玩家名（账号当前值）。</summary>
    public required string DisplayName { get; init; }
}
