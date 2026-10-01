using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人对无合法选项的裁定点作出决定（R-0009 自由决定路径）。</summary>
public sealed record ResolveDecisionPointCommand : GameCommand
{
    /// <summary>被了结的裁定点。</summary>
    public required DecisionPointId DecisionPointId { get; init; }

    /// <summary>说书人的决定。</summary>
    public string? Decision { get; init; }

    /// <summary>说明。</summary>
    public string? Note { get; init; }
}
