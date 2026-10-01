namespace OpenClockTower.Kernel;

/// <summary>本阶段计划已走完（所有槽位都走过了）。</summary>
public sealed record PhaseCompletedEvent : GameEvent
{
    /// <summary>走完的计划标识。</summary>
    public required string PlanLabel { get; init; }
}
