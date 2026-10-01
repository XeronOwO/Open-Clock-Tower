namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人对某名玩家下达了一条「疯狂地证明自己是 X」的要求。
/// </summary>
/// <remarks>
/// 依据 R-0003：疯狂不是引擎状态，引擎**不判定**玩家是否疯狂，只记录"说过什么要求"。
/// 因此这条事件只往状态账里加一条要求，不改变任何其它维度。
/// 产生方是裁定点（<see cref="DecisionPoint"/>）的落地路径，该路径随说书人裁定面一起交付。
/// </remarks>
public sealed record MadnessRequirementIssuedEvent : GameEvent
{
    /// <summary>被下达的要求。</summary>
    public required MadnessRequirement Requirement { get; init; }
}
