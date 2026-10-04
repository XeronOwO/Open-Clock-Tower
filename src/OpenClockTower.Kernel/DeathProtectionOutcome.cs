namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护查询的结论四态（R-0048：受保护 / 不受保护 / 待说书人裁定 / 判定不了）。
/// </summary>
/// <remarks>
/// 四态而不是 bool：<see cref="NeedsRuling"/> 与 <see cref="Indeterminate"/> 都是"现在不能落死亡"，
/// 但补救动作不同（先说书人裁定 vs 先补观测）；混成一个 false 就会变成静默死亡（D-0015：不猜）。
/// </remarks>
public enum DeathProtectionOutcome
{
    /// <summary>不受保护：死亡照常发生。</summary>
    NotProtected,

    /// <summary>受保护：不产生死亡、不触发死亡触发能力（R-0045 第 3 条）。</summary>
    Protected,

    /// <summary>待说书人裁定：裁定之后才有结论；收口方显式拒绝并提示先裁定（R-0048 第 2 条）。</summary>
    NeedsRuling,

    /// <summary>判定不了（事实没观测齐）：既不死亡也不放行，收口方显式拒绝（D-0015：不猜）。</summary>
    Indeterminate,
}
