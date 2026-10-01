namespace OpenClockTower.Application;

/// <summary>说书人强推当前槽位：越过剩余配额与挂起，直接进入下一槽位（D-0014 兜底）。</summary>
public sealed record ForceAdvanceCommand : GameCommand
{
    /// <summary>强推原因（进事件流与审计）。</summary>
    public required string Reason { get; init; }
}
