namespace OpenClockTower.Kernel;

/// <summary>
/// 「能力未正常生效」的原因分类（数学家要靠它给出数字）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0004：计数口径尚未逐条核对完，
/// **未核对到依据的路径一律记 <see cref="Open"/>**（未定）并计入待核对清单；新增分类前先改 R-0004。
/// </para>
/// <para>
/// 枚举值与 R-0004 的分类逐项对应：中毒 / 醉酒 / 相克 / 涡流 / 咖啡师 / 能力自身设定 / 说书人裁定 / 未定。
/// </para>
/// </remarks>
public enum MalfunctionKind
{
    /// <summary>未定：口径尚未核对到依据（R-0004）。取 0 作默认值，漏标不会被静默放过。</summary>
    Open = 0,

    /// <summary>因中毒而未正常生效。</summary>
    Poisoned,

    /// <summary>因醉酒而未正常生效。</summary>
    Drunk,

    /// <summary>因相克规则而未正常生效；依据 R-0004，此类**不计入**数学家的统计。</summary>
    Jinx,

    /// <summary>因涡流（恶魔）而获得错误信息。</summary>
    Vortox,

    /// <summary>因咖啡师（旅行者）的效果而被干扰。</summary>
    Barista,

    /// <summary>能力自身设定的正常结果（如占卜师因干扰项而得知「是」，不算异常）。</summary>
    AbilityDesign,

    /// <summary>说书人裁定导致的结果。</summary>
    StorytellerRuling,
}
