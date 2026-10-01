using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 建表结果：成功给计划，失败给可机读的原因码——**不猜、不静默跳过**。
/// </summary>
public sealed record NightPlanOutcome
{
    /// <summary>成功时的计划。</summary>
    public StepPlan? Plan { get; init; }

    /// <summary>失败时的原因码（如 <c>plan.seat_unassigned</c>）。</summary>
    public string? FailureCode { get; init; }

    /// <summary>失败说明（给日志与说书人定位用）。</summary>
    public string? FailureMessage { get; init; }

    /// <summary>构造成功结果。</summary>
    public static NightPlanOutcome Success(StepPlan plan) => new() { Plan = plan };

    /// <summary>构造失败结果。</summary>
    public static NightPlanOutcome Failure(string code, string message) =>
        new() { FailureCode = code, FailureMessage = message };
}
