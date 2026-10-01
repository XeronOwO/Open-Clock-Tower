namespace OpenClockTower.Kernel;

/// <summary>
/// 中毒。与「醉酒」是两件独立的事，**互不抵消**。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-3。
/// 注意：中毒/醉酒**不是角色的状态而是玩家的状态**——"如果一名中毒的玩家与其他玩家
/// 交换了角色，他仍然处于中毒状态。"
/// </remarks>
public enum PoisonState
{
    /// <summary>健康。</summary>
    Healthy,

    /// <summary>中毒。</summary>
    Poisoned,
}
