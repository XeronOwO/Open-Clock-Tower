namespace OpenClockTower.Kernel;

/// <summary>
/// 一条「待定的死亡」：麻脸巫婆之夜的窗口期内，恶魔击杀先记在这里，由说书人裁定确认或阻止。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0030 第 2 条：窗口内恶魔击杀不直接产生死亡事实——
/// 规则的原文是「说书人能够自由决定是否让某名玩家死亡，或让被恶魔攻击的某名玩家存活」
/// （百科《麻脸巫婆》· 2026-10-01 抓取 · 规则细节 1）。
/// </remarks>
public sealed record DeferredDeath
{
    /// <summary>被攻击的席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>发起击杀的恶魔席位（裁定「确认」时作为死亡事实的归因来源）。</summary>
    public required SeatId Source { get; init; }

    /// <summary>发起击杀的能力标识。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>发生位置与依据说明（进审计与说书人视图）。</summary>
    public required string Note { get; init; }
}
