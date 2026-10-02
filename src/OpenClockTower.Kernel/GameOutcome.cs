namespace OpenClockTower.Kernel;

/// <summary>
/// 一局的胜负结论：谁赢了、因为哪条条件、以及人类可读的说明。
/// </summary>
/// <remarks>
/// 它是纯计算的结果（<see cref="OutcomeEvaluator"/>），落进事件流时由
/// <see cref="GameEndedEvent"/> 承载；判定后不再改动（撤销 = 截断重放，D-0010）。
/// </remarks>
public sealed record GameOutcome
{
    /// <summary>获胜阵营。</summary>
    public required Alignment Winner { get; init; }

    /// <summary>触发的条件分类。</summary>
    public required OutcomeCondition Condition { get; init; }

    /// <summary>人类可读说明（进事件流与投影，用于结束横幅与审计）。</summary>
    public required string Detail { get; init; }
}
