namespace OpenClockTower.Kernel;

/// <summary>一次提名被发起：进入该提名的投票窗口。</summary>
/// <remarks>
/// 发起者与目标是否合法由内核在产出本事件前判定（只有存活者可发起、同日一次、同一时间一项）。
/// 依据百科《提名》· 2026-10-01 抓取。
/// </remarks>
public sealed record NominationMadeEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>当天第几次提名（从 1 起）。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>发起提名的席位。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位。</summary>
    public required SeatId Nominee { get; init; }
}
