namespace OpenClockTower.Kernel;

/// <summary>无合法选项且声明为跳过（R-0009）：本槽位不发请求，但**照样走完配额**。</summary>
public sealed record PromptSkippedEvent : GameEvent
{
    /// <summary>被跳过的槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>跳过原因（含声明行为，便于事后审计）。</summary>
    public required string Reason { get; init; }
}
