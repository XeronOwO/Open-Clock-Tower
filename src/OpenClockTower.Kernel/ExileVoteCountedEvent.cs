namespace OpenClockTower.Kernel;

/// <summary>一次流放完成计票：票面冻结并给出结论（R-0044 第 5 / 9 条）。</summary>
/// <remarks>
/// <para>
/// 本事件携带结论而不让折叠层重算：阈值分母是收票开始时的在局人数快照（<see cref="VoteSweepState.Seats"/>），
/// 且死亡保护是 D3 的收口判定（D-0010：事件是唯一事实来源，状态是折叠结果）。
/// </para>
/// <para>
/// 达线且目标存活时，同一批另有一条 <see cref="SeatStateChangedEvent"/> 记录死亡（流放死亡是真实死亡，
/// R-0045 第 1 条）；目标已死时只记结论、不重复记死亡。
/// </para>
/// </remarks>
public sealed record ExileVoteCountedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几条流放。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>计票时的最终赞成票名单，按席位号升序。</summary>
    public required IReadOnlyList<SeatId> Voters { get; init; }

    /// <summary>计票结论（达线 / 未达线；保护分支随 D3）。</summary>
    public required ExileConclusion Conclusion { get; init; }
}
