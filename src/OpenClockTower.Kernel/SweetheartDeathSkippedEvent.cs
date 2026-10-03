namespace OpenClockTower.Kernel;

/// <summary>
/// 心上人的死亡触发没有产生效果：死亡时能力未生效（醉酒 / 中毒，或这两维未观测），
/// 或说书人未裁定（强推 / 收口）。
/// </summary>
/// <remarks>
/// **可归因的跳过**：事件进流、原因可查；有它之后不再为同一名心上人的死亡重复开裁定（R-0039）。
/// </remarks>
public sealed record SweetheartDeathSkippedEvent : GameEvent
{
    /// <summary>本应产生醉酒的心上人席位。</summary>
    public required SeatId Sweetheart { get; init; }

    /// <summary>跳过原因（人类可读，进审计与说书人视图）。</summary>
    public required string Reason { get; init; }
}
