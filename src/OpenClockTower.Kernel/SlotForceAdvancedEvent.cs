namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人强推：越过配额与挂起，直接进入下一槽位（D-0014 兜底）。
/// </summary>
/// <remarks>
/// 与自动推进分开建模，是为了让"人工改变了节奏"永久留在事件流里：可审计、可重放。
/// </remarks>
public sealed record SlotForceAdvancedEvent : GameEvent
{
    /// <summary>离开的槽位下标。</summary>
    public required int FromIndex { get; init; }

    /// <summary>进入的槽位下标；等于槽位总数表示本计划走完。</summary>
    public required int ToIndex { get; init; }

    /// <summary>强推原因（谁操作的由应用层审计记录）。</summary>
    public required string Reason { get; init; }
}
