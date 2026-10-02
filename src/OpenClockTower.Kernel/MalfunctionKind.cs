namespace OpenClockTower.Kernel;

/// <summary>
/// 「能力未正常生效」的原因分类（数学家要靠它给出数字）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0004（已闭合）：逐条对表在裁定里；哪些分类计入数学家的数字
/// 见 <see cref="MalfunctionCounting"/>。
/// </para>
/// <para>
/// 一次结算可以同时命中多条路径（例如涡流叠加中毒）：账本**逐条记录、互不顶替**；
/// 数学家的数字按玩家去重（R-0004 第 1 条）。
/// </para>
/// </remarks>
public enum MalfunctionKind
{
    /// <summary>未定：还没有核对到依据的路径（R-0004 仅剩咖啡师 / 说书人裁定待核对）。取 0 作默认值，漏标不会被静默放过。</summary>
    Open = 0,

    /// <summary>因中毒而未正常生效；R-0004：计入数学家的统计。</summary>
    Poisoned,

    /// <summary>因醉酒而未正常生效；R-0004：计入数学家的统计。</summary>
    Drunk,

    /// <summary>因相克规则而未正常生效；依据 R-0004，此类**不计入**数学家的统计。</summary>
    Jinx,

    /// <summary>因涡流（恶魔）而获得错误信息；R-0004：计入数学家的统计（计玩家，不计次数）。</summary>
    Vortox,

    /// <summary>因咖啡师（旅行者）的效果而被干扰；R-0004 尚未核对（该页未抓取），引擎不得自行写此类记录。</summary>
    Barista,

    /// <summary>能力自身设定的正常结果（如占卜师因干扰项而得知「是」）；依据 R-0004，**不计入**数学家的统计。</summary>
    AbilityDesign,

    /// <summary>说书人裁定导致的结果；R-0004 尚未核对（当前没有实现路径），用到时先补裁定表。</summary>
    StorytellerRuling,
}
