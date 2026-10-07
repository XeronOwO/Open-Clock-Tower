namespace OpenClockTower.Contracts;

/// <summary>
/// 一条待说书人裁定的旅行者离场申请（D-0037）：只说书人视图里有它。
/// </summary>
/// <remarks>
/// 玩家的申请理由只说书人与申请人本人可见，不进任何公开投影（D-0012 §4.3）。
/// </remarks>
public sealed record DepartureRequestDto
{
    /// <summary>申请离场的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>旅行者给出的理由；可为 null。</summary>
    public string? Note { get; init; }
}
