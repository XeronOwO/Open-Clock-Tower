namespace OpenClockTower.Rules;

/// <summary>
/// 夜晚顺序表的口径变体。
/// </summary>
/// <remarks>
/// 依据 docs/standard/rulings.md R-0014：百科同时给出「原本顺序」（剧本页）与
/// 「推荐顺序」（《夜晚行动顺序一览》），并明说**说书人可以自行选择**执行哪一种。
/// 本项目两套都入库；默认口径与选择入口由上层（结算引擎 / 说书人面板）决定。
/// </remarks>
public enum NightOrderVariant
{
    /// <summary>原本顺序：百科《梦殒春宵》页「夜晚顺序表」。</summary>
    Original,

    /// <summary>推荐顺序：百科《夜晚行动顺序一览》「首个夜晚 / 其他夜晚」。</summary>
    Recommended,
}
