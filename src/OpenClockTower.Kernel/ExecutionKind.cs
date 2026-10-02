namespace OpenClockTower.Kernel;

/// <summary>
/// 一次处决的来源分类：提名流程的常规处决，或两名角色的疯狂处罚处决。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：处罚处决与常规处决共用「每个白天最多一次处决」这条账，
/// 但夜晚发生的处罚处决**不**占任何白天的上限，因此处决事实必须带上来源分类，
/// 重放与说书人视图才分得清"这一天的处决是怎么来的"。
/// </remarks>
public enum ExecutionKind
{
    /// <summary>提名流程的常规处决（发生在白天）。</summary>
    Day = 0,

    /// <summary>洗脑师的疯狂处罚处决（R-0020 / R-0021）。</summary>
    CerenovusMadness,

    /// <summary>畸形秀演员的疯狂处罚处决（R-0020；百科《畸形秀演员》）。</summary>
    MutantMadness,
}
