namespace OpenClockTower.Kernel;

/// <summary>
/// 持续型效果的终止原因分类。
/// </summary>
/// <remarks>
/// 依据百科《术语汇总》「死亡」与《重要细节》二-7：来源死亡、或来源换了角色而失去原角色能力时，
/// 「其角色能力所产生的任何持续性的效果也会立即终止」。说书人强制作废属于 D-0014 的兜底能力。
/// 终止**不可逆**：来源之后复活或以新角色回来，已终止的效果也不恢复。
/// </remarks>
public enum EffectTerminationKind
{
    /// <summary>来源死亡。</summary>
    SourceDied = 0,

    /// <summary>来源的角色发生变化，原角色能力不再存在。</summary>
    SourceLostAbility,

    /// <summary>说书人强制作废（D-0014 兜底）。</summary>
    StorytellerVoided,

    /// <summary>
    /// 常驻效果重算：条件不再满足（例如诺-达鲺的邻近镇民换了人），由引擎终止并补新的效果。
    /// 与"来源失效"不同——这里终结的是"这条效果的对象 / 条件"，来源本身可能还活着。
    /// </summary>
    NoLongerApplies,

    /// <summary>
    /// 席位以旅行者身份离场：角色与生命标记一并移除，以它为来源 / 目标的持续型效果与疯狂要求终止
    /// （百科《旅行者》· 2026-10-04 抓取 · 离开流程；`rulings.md` R-0044 第 6 条）。
    /// 新值追加在末尾，不改既有枚举序号（事件载荷按序号序列化）。
    /// </summary>
    SeatLeftGame,
}
