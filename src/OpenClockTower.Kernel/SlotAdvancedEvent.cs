namespace OpenClockTower.Kernel;

/// <summary>自动推进：配额已走完且没有挂起，步骤机进入下一槽位。</summary>
public sealed record SlotAdvancedEvent : GameEvent
{
    /// <summary>离开的槽位下标。</summary>
    public required int FromIndex { get; init; }

    /// <summary>进入的槽位下标；等于槽位总数表示本计划走完。</summary>
    public required int ToIndex { get; init; }
}
