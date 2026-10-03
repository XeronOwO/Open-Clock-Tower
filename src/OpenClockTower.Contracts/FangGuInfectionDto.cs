namespace OpenClockTower.Contracts;

/// <summary>
/// 方古的「限一次」整局事实（<c>docs/standard/rulings.md</c> R-0034）：只说书人视图可见。
/// </summary>
/// <remarks>
/// 说书人据此在魔典中心显示「限一次」标记（百科《方古》· 2026-10-01 抓取 · 提示标记）；
/// 玩家投影里没有这个字段（D-0012 §4.3）。
/// </remarks>
public sealed record FangGuInfectionDto
{
    /// <summary>被侵染、变成新方古的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>发起侵染的原方古席位。</summary>
    public required int Source { get; init; }

    /// <summary>记账说明（进审计与说书人视图）。</summary>
    public required string Note { get; init; }
}
