namespace OpenClockTower.Kernel;

/// <summary>
/// 规则层对「该席位这次死亡是否被保护」的判定结论与说明。
/// </summary>
/// <remarks>
/// 与 <see cref="AdjudicatedExecutionEligibility"/> 同族：结论 + 人可读说明；
/// 说明要写清"差什么"（待裁定 / 判定不了），它是收口拒绝文案的来源（R-0048）。
/// </remarks>
public sealed record DeathProtectionAssessment
{
    /// <summary>判定结论。</summary>
    public required DeathProtectionOutcome Outcome { get; init; }

    /// <summary>人可读说明：受保护 / 不受保护 / 待裁定 / 判定不了的依据。</summary>
    public required string Note { get; init; }
}
