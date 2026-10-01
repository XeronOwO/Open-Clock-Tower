namespace OpenClockTower.Kernel;

/// <summary>常驻效果求值结论：要么给出确定的期望集合，要么明确说「这次判定不了」。</summary>
/// <remarks>
/// <see cref="IsConclusive"/> 为 false 时对账**什么都不做**（既不补也不终止）——
/// 输入不全时的正确行为是「宁可不动」，不是「按缺失值猜一套」（D-0015）。
/// </remarks>
public sealed record StandingEffectAssessment
{
    /// <summary>本次是否给出了确定结论。</summary>
    public required bool IsConclusive { get; init; }

    /// <summary>确定的期望集合；<see cref="IsConclusive"/> 为 false 时为空。</summary>
    public IReadOnlyList<StandingEffectExpectation> Expectations { get; init; } = [];

    /// <summary>判定不了的说明（进了哪条缺口的账）。</summary>
    public string? Note { get; init; }

    /// <summary>判定不了：本次不重算。</summary>
    public static StandingEffectAssessment Inconclusive(string note) =>
        new() { IsConclusive = false, Note = note };

    /// <summary>确定结论：这些就是此刻应当存在的持续型效果。</summary>
    public static StandingEffectAssessment Conclusive(IReadOnlyList<StandingEffectExpectation> expectations) =>
        new() { IsConclusive = true, Expectations = expectations };
}
