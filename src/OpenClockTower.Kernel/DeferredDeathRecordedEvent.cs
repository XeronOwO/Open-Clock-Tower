namespace OpenClockTower.Kernel;

/// <summary>
/// 窗口期内的恶魔击杀被记为**待定死亡**（不直接致死），等说书人裁定（R-0030 第 2 条）。
/// </summary>
public sealed record DeferredDeathRecordedEvent : GameEvent
{
    /// <summary>被攻击的席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>发起击杀的恶魔席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>发起击杀的能力标识。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>发生位置与依据说明。</summary>
    public required string Note { get; init; }

    /// <summary>
    /// 确认时改为「转化」的载荷（方古侵染外来者）；null = 普通击杀。
    /// </summary>
    /// <remarks>
    /// 依据：百科《方古》· 2026-10-01 抓取 · 角色简介 2——「方古首次攻击并成功杀死外来者时，
    /// 改为方古死亡，外来者变成邪恶的方古」。平台口径见 <c>docs/standard/rulings.md</c> R-0034。
    /// </remarks>
    public DeferredTransformation? Transformation { get; init; }
}
