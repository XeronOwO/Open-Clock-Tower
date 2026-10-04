namespace OpenClockTower.Kernel;

/// <summary>规则层对「有没有可用屠夫」的判定结论与说明（R-0050）。</summary>
/// <remarks>
/// 与 <see cref="DeathProtectionAssessment"/> 同族：结论 + 人可读说明；
/// 说明要写清"差什么"（观测不齐 / 多个屠夫席位），它是收口拒绝文案的来源。
/// </remarks>
public sealed record ExtraNominationAssessment
{
    /// <summary>判定结论。</summary>
    public required ExtraNominationOutcome Outcome { get; init; }

    /// <summary>人可读说明：可用 / 不可用 / 判定不了差什么。</summary>
    public required string Note { get; init; }

    /// <summary><see cref="ExtraNominationOutcome.Available"/> 时的窗口授予席位。</summary>
    public SeatId? Seat { get; init; }
}
