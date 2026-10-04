namespace OpenClockTower.Kernel;

/// <summary>说书人开始一次钟盘收票：进入倒计时，随后分针逐席旋转收票（R-0017 目标形态）。</summary>
/// <remarks>
/// 倒计时与间隔是**呈现参数**：随事件流持久化，供重连 / 重启 / 重建复现节奏；判定完全不读它们。
/// 事件同时携带开始时的完整座次快照，折叠层据此校验收票顺序（D-0010）。
/// </remarks>
public sealed record VoteSweepStartedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几次提名。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>收票顺序（完整座次，按席位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>倒计时长度（毫秒）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒）。</summary>
    public required int IntervalMilliseconds { get; init; }
}
