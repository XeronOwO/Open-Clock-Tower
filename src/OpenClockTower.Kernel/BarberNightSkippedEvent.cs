namespace OpenClockTower.Kernel;

/// <summary>
/// 理发师死亡但没有开出「今晚理发」：死亡时醉酒 / 中毒，或死亡时点已经错过当夜的理发师格。
/// </summary>
/// <remarks>
/// 与 <see cref="KlutzChoiceSkippedEvent"/> 同族：能力本来就不该生效（或时机已过）时留下一条
/// 可归因的显式记录，而不是让分支静默消失（平台口径见 <c>docs/standard/rulings.md</c> R-0033）。
/// 本事件不改步骤机状态，只在事件流里留痕。
/// </remarks>
public sealed record BarberNightSkippedEvent : GameEvent
{
    /// <summary>死亡 / 换角的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>跳过原因（醉酒 / 中毒 / 过时不候 / 阶段未开始等）。</summary>
    public required string Reason { get; init; }
}
