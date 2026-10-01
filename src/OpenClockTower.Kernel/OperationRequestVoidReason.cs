namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求的作废原因。
/// </summary>
/// <remarks>
/// 依据 D-0011 硬约束 3：内核里**没有"超时/过期时间"这个概念**，只有作废原因。
/// </remarks>
public enum OperationRequestVoidReason
{
    /// <summary>说书人强制作废（无超时的主要对冲手段）。</summary>
    StorytellerForce,

    /// <summary>说书人强推 / 接管切步时了结了该请求（D-0014 兜底）。</summary>
    StorytellerTakeover,

    /// <summary>座位依赖不再满足（目标死亡、角色变更等上游变化）。</summary>
    DependencyViolated,

    /// <summary>阶段已推进，请求失去意义（结构性防御，正常流程不会走到）。</summary>
    PhaseAdvanced,

    /// <summary>被上游新请求取代。</summary>
    Superseded,
}
