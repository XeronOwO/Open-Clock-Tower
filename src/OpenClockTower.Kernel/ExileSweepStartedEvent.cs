namespace OpenClockTower.Kernel;

/// <summary>说书人开始一次流放表决的钟盘收票（R-0044 第 10 条沿用 R-0017 的收票机制）。</summary>
/// <remarks>
/// <para>
/// <see cref="Seats"/> 是开始收票那一刻的**在局座次快照**：既是收票顺序，也是本次阈值的分母
/// （R-0044 第 5 / 6 条：「以本次表决开始收票时的在局玩家为准」，已离场者不计）。
/// </para>
/// <para>
/// 倒计时与间隔是**呈现参数**：随事件流持久化，供重连 / 重启 / 重建复现节奏；判定完全不读它们。
/// </para>
/// </remarks>
public sealed record ExileSweepStartedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几条流放。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>收票顺序（开始时的在局座次，按席位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>倒计时长度（毫秒）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒）。</summary>
    public required int IntervalMilliseconds { get; init; }
}
