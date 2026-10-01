namespace OpenClockTower.Kernel;

/// <summary>一个阶段的步骤机开始按新计划推进。</summary>
public sealed record PhaseStartedEvent : GameEvent
{
    /// <summary>本阶段的完整步骤表（含空槽位）。</summary>
    public required StepPlan Plan { get; init; }

    /// <summary>阶段开始时的控制模式（说书人接管下开启的新阶段仍然是接管）。</summary>
    public required ControlMode Control { get; init; }
}
