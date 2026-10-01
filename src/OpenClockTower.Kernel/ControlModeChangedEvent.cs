namespace OpenClockTower.Kernel;

/// <summary>控制模式变化：自动 → 说书人接管，或交还自动化（D-0014）。</summary>
public sealed record ControlModeChangedEvent : GameEvent
{
    /// <summary>新的控制模式。</summary>
    public required ControlMode Mode { get; init; }

    /// <summary>变更原因（接管 / 交还都由说书人给出，必须留痕）。</summary>
    public required string Reason { get; init; }
}
