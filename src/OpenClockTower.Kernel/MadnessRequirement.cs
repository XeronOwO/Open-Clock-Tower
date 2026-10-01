namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人对某名玩家下达的「疯狂地证明自己是 X」要求。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0003：疯狂**不是**引擎状态——引擎不判定玩家是否疯狂，
/// 因此它不出现在 <see cref="SeatState"/> 里；它只由裁定写入（<see cref="IssuedBy"/> 指向产生它的裁定点）。
/// 引擎只负责产生要求、承接裁定结果并触发后果。
/// </para>
/// <para>
/// 「在何时」由裁定点在事件流中的位置承载（裁定点进事件流即可回放，见
/// <c>docs/architecture/current.md</c> §2.4）；内核不使用真实时间（D-0008）。
/// </para>
/// </remarks>
public sealed record MadnessRequirement
{
    /// <summary>被要求疯狂证明什么的玩家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>被要求疯狂证明的内容（例如某个角色或角色类型）。</summary>
    public required string ProveToBe { get; init; }

    /// <summary>写入这条要求的裁定点；只有裁定能写疯狂要求（R-0003）。</summary>
    public required DecisionPointId IssuedBy { get; init; }
}
