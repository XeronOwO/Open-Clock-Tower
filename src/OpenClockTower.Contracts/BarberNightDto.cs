namespace OpenClockTower.Contracts;

/// <summary>
/// 「今晚理发」待处理事实（<c>docs/standard/rulings.md</c> R-0033）：只说书人视图可见。
/// </summary>
/// <remarks>
/// 理发师死亡后由恶魔在当夜交互（换角结果由既有的角色变更事件表达）；事实跨阶段保留，
/// 说书人据此知道"今晚还有一次理发交互"。玩家投影里没有这个字段（D-0012 §4.3）。
/// </remarks>
public sealed record BarberNightDto
{
    /// <summary>以理发师身份死亡的席位（能力来源）。</summary>
    public required int Source { get; init; }

    /// <summary>记账说明（谁以什么方式死亡触发）。</summary>
    public required string Note { get; init; }
}
