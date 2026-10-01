namespace OpenClockTower.Kernel;

/// <summary>说书人对裁量点作出了决定（自由决定，不受选项集合约束）。</summary>
public sealed record DecisionPointResolvedEvent : GameEvent
{
    /// <summary>被了结的裁定点。</summary>
    public required DecisionPointId DecisionPointId { get; init; }

    /// <summary>说书人的决定（可为空或自由文本——这类裁定点本就没有合法选项集合）。</summary>
    public string? Decision { get; init; }

    /// <summary>说明（例如强推时"由说书人接管"）。</summary>
    public string? Note { get; init; }
}
