namespace OpenClockTower.Contracts;

/// <summary>状态账里的一条维度事实：哪个维度、当前值、怎么来的（说书人视角）。</summary>
public sealed record SeatStateFactDto
{
    /// <summary>维度名：Life / Character / Alignment / Drunk / Poison。</summary>
    public required string Dimension { get; init; }

    /// <summary>当前已知的值（枚举名或角色 slug）。</summary>
    public required string Value { get; init; }

    /// <summary>这条事实的来由。</summary>
    public required string Reason { get; init; }

    /// <summary>导致这条事实的席位；无人可归因时为空。</summary>
    public int? CausedBy { get; init; }
}
