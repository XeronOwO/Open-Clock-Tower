namespace OpenClockTower.Kernel;

/// <summary>一条流放提议的状态。</summary>
/// <remarks>
/// 与 <see cref="NominationStatus"/> 分开：流放不是提名（R-0044 第 1 条），两种选票的账各自独立；
/// 状态只表达「还在表决 / 已计票」这一条事实。
/// </remarks>
public enum ExileStatus
{
    /// <summary>已提议、尚未计票（含收票进行中）。</summary>
    Voting,

    /// <summary>已计票：结论见 <see cref="ExileRecord.Conclusion"/>。</summary>
    Counted,
}
