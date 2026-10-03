namespace OpenClockTower.Kernel;

/// <summary>
/// 贤者的死亡触发没有发生：死因不是恶魔击杀，或当夜贤者格已过 / 没有该格。
/// </summary>
/// <remarks>
/// 这是**可归因的跳过**，不是静默缺失：事件进流、原因可查；它不改步骤机状态
/// （与 <see cref="BarberNightSkippedEvent"/> 同族）。口径见 <c>docs/standard/rulings.md</c> R-0038。
/// </remarks>
public sealed record SageNightSkippedEvent : GameEvent
{
    /// <summary>以贤者身份死亡、但没有触发能力的席位。</summary>
    public required SeatId Sage { get; init; }

    /// <summary>跳过原因（人类可读，进审计与说书人视图）。</summary>
    public required string Reason { get; init; }
}
