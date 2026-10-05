namespace OpenClockTower.Contracts;

/// <summary>一条公开猜测的线上形状（R-0057-B）：猜哪个席位是哪个角色。</summary>
public sealed record JugglerGuessDto
{
    /// <summary>被猜的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>猜的角色 slug（可以是场上没有的角色）。</summary>
    public required string Character { get; init; }
}
