namespace OpenClockTower.Kernel;

/// <summary>白天阶段开始：白天账里新开一天。</summary>
/// <remarks>
/// 与 <see cref="PhaseStartedEvent"/>（步骤机进入新计划）配套：后者描述步骤机，
/// 本事件描述白天账（第几天、从此开始可以提名）。
/// </remarks>
public sealed record DayStartedEvent : GameEvent
{
    /// <summary>这是第几天（1 = 首个白天）。</summary>
    public required int DayNumber { get; init; }
}
