namespace OpenClockTower.Kernel;

/// <summary>
/// 呆瓜的死亡选择没有发生：能力未生效（醉酒 / 中毒），或说书人强制作废（R-0027 第 4–5 条）。
/// </summary>
/// <remarks>
/// 这是**可归因的跳过**，不是静默缺失：事件进流、原因可查；有它之后，呆瓜的触发器不会在后续黎明重复开选择。
/// </remarks>
public sealed record KlutzChoiceSkippedEvent : GameEvent
{
    /// <summary>本应做出选择的呆瓜席位。</summary>
    public required SeatId Klutz { get; init; }

    /// <summary>跳过原因（人类可读，进审计与说书人视图）。</summary>
    public required string Reason { get; init; }
}
