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

    /// <summary>
    /// 确认时改为「转化」的载荷（方古侵染外来者）；null = 普通击杀。
    /// </summary>
    /// <remarks>
    /// 依据与平台口径见 <see cref="DeferredTransformation"/> 与 <c>docs/standard/rulings.md</c> R-0034。
    /// </remarks>
    public DeferredTransformation? Transformation { get; init; }

    /// <summary>
    /// 确认时**先于死亡**落下的「保留能力」载荷（亡骨魔杀死的爪牙）；null = 不含保留能力。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Transformation"/> 互斥；口径见 <see cref="DeferredRetention"/> 与
    /// <c>docs/standard/rulings.md</c> R-0056。
    /// </remarks>
    public DeferredRetention? Retention { get; init; }
}
