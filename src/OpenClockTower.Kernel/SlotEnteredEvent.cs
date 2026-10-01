namespace OpenClockTower.Kernel;

/// <summary>步骤机进入一个槽位；配额从这一刻开始走。</summary>
public sealed record SlotEnteredEvent : GameEvent
{
    /// <summary>槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>槽位标识。</summary>
    public required StepSlotId SlotId { get; init; }
}
