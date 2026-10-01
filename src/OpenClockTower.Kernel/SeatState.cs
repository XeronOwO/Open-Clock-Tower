namespace OpenClockTower.Kernel;

/// <summary>
/// 一名玩家席位在某一时刻的全部状态。
/// </summary>
/// <remarks>
/// <para>
/// 硬约束（依据：百科《重要细节》三——"状态与玩家绑定，而不与角色绑定"，
/// "在游戏开始后，玩家的不同状态之间相互独立，互不干涉"）：
/// <see cref="Character"/>、<see cref="Alignment"/>、<see cref="Life"/>、
/// <see cref="Drunk"/>、<see cref="Poison"/> 之间**不得存在任何联合约束**。
/// 一个维度改变不得带动另一个。任何形如"角色变了顺手清个醉酒"的写法都是 bug，
/// 而且是最难定位的那一类。
/// </para>
/// <para>
/// 「疯狂」刻意**不在**这里：依据 <c>docs/standard/rulings.md</c> R-0003，
/// 疯狂是现实中的状态、由说书人裁定，引擎不判定它。它作为裁定结果单独建模。
/// </para>
/// </remarks>
public sealed record SeatState
{
    /// <summary>该席位当前持有的角色。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>该席位当前所属阵营。</summary>
    public required Alignment Alignment { get; init; }

    /// <summary>存活或死亡。</summary>
    public required LifeState Life { get; init; }

    /// <summary>醉酒或清醒。</summary>
    public required DrunkState Drunk { get; init; }

    /// <summary>中毒或健康。</summary>
    public required PoisonState Poison { get; init; }
}
