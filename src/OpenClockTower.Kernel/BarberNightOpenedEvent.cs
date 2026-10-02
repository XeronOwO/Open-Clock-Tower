namespace OpenClockTower.Kernel;

/// <summary>
/// 「今晚理发」事实开启：理发师死亡、且死亡时未醉酒 / 未中毒
/// （百科《理发师》· 2026-10-01 抓取 · 提示标记「今晚理发」的放置条件）。
/// </summary>
/// <remarks>
/// 由规则层的死亡触发器在死亡的**同一批**产出：死亡触发立即记账，与恶魔的交互等到当夜理发师格
/// （百科《死亡触发能力》· 2026-10-01 抓取 · 能力简介）。平台口径见
/// <c>docs/standard/rulings.md</c> R-0033。
/// </remarks>
public sealed record BarberNightOpenedEvent : GameEvent
{
    /// <summary>以理发师身份死亡的席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>开启说明（死亡时点的判定依据），进审计与说书人视图。</summary>
    public required string Note { get; init; }
}
