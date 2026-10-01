namespace OpenClockTower.Kernel;

/// <summary>无合法选项且声明为阻塞报警（R-0009）：暂停自动推进，等说书人处理。</summary>
public sealed record SlotBlockedEvent : GameEvent
{
    /// <summary>被阻塞的槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>阻塞原因。</summary>
    public required string Reason { get; init; }
}
