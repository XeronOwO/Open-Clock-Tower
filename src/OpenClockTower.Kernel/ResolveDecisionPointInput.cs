namespace OpenClockTower.Kernel;

/// <summary>说书人对当前挂起裁定点作出决定（R-0009 自由决定路径）。</summary>
public sealed record ResolveDecisionPointInput : StepMachineInput
{
    /// <summary>被了结的裁定点（必须是当前挂起的那个）。</summary>
    public required DecisionPointId DecisionPointId { get; init; }

    /// <summary>说书人的决定（自由文本或空）。</summary>
    public string? Decision { get; init; }

    /// <summary>说明。</summary>
    public string? Note { get; init; }
}
