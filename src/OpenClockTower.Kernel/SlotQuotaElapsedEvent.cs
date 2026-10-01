namespace OpenClockTower.Kernel;

/// <summary>当前槽位的最短配额已走完（由宿主节拍器按服务端时钟判定后输入内核）。</summary>
public sealed record SlotQuotaElapsedEvent : GameEvent
{
    /// <summary>配额走完的槽位。</summary>
    public required StepSlotId SlotId { get; init; }
}
