namespace OpenClockTower.Kernel;

/// <summary>
/// 胜利条件的分类：常规（核心规则给双方各一条）与特殊（角色能力造成的胜负）。
/// </summary>
/// <remarks>
/// 依据：百科《特殊胜利失败条件》· 2026-10-01 抓取（常规方式 + 特殊条件 + 优先级）；
/// 优先级与判定时机的项目口径见 <c>docs/standard/rulings.md</c> R-0024。
/// </remarks>
public enum OutcomeCondition
{
    /// <summary>常规 · 善良：所有恶魔均死亡。</summary>
    DemonsAllDead,

    /// <summary>常规 · 邪恶：场上仅剩两名玩家存活（旅行者不计入，首版无旅行者）。</summary>
    TwoPlayersAlive,

    /// <summary>特殊 · 邪恶：镜像双子中善良阵营的一方被处决。</summary>
    EvilTwinGoodTwinExecuted,

    /// <summary>特殊 · 邪恶：涡流存活且黄昏时今天无人被处决。</summary>
    VortoxNoExecution,

    /// <summary>特殊 · 呆瓜选择：选中邪恶玩家 → 呆瓜当时所在阵营落败，对侧获胜。</summary>
    KlutzChoiceFactionLoses,
}
