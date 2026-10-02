namespace OpenClockTower.Kernel;

/// <summary>
/// 一次提名的状态：投票窗口开着 / 已计票。
/// </summary>
/// <remarks>
/// 依据百科《提名》术语介绍：同一时间只能有一名玩家被提名，
/// 一项提名已被发起但处决投票尚未结束时不能再提下一项。
/// </remarks>
public enum NominationStatus
{
    /// <summary>提名已发起、投票窗口开着（尚未计票）。</summary>
    Voting,

    /// <summary>已计票（票面冻结，不能再投）。</summary>
    Counted,
}
