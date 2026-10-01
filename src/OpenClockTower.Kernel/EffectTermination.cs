namespace OpenClockTower.Kernel;

/// <summary>
/// 一条持续型效果的终止事实：**因为什么、被谁**结束。
/// </summary>
/// <remarks>
/// 终止必须带原因——"效果没了"却不写为什么，等于把上帝视角最需要的那一步留白，
/// 而这正是「投毒者死了，所以他下的毒解了」要被回答的地方（票据第 2 条）。
/// </remarks>
public sealed record EffectTermination
{
    /// <summary>终止原因分类。</summary>
    public required EffectTerminationKind Kind { get; init; }

    /// <summary>人可读的终止说明（带上下文，供说书人直接看）。</summary>
    public required string Reason { get; init; }

    /// <summary>导致终止的席位；没有特定导致方时为空。</summary>
    public SeatId? CausedBy { get; init; }
}
