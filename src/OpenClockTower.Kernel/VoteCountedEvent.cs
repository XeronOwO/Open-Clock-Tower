namespace OpenClockTower.Kernel;

/// <summary>一次提名完成计票：票面冻结，并给出计票后的「即将被处决」状态。</summary>
/// <remarks>
/// <para>
/// 依据百科《投票》· 2026-10-01 抓取：只由计票改写「即将被处决」；
/// 计票后即使场上状况变化也不再重新判定。
/// </para>
/// <para>
/// 本事件携带计票结论而不让折叠层重算：票数阈值依赖**计票那一刻**的存活人数，
/// 折叠层只有事件流、没有生命事实（D-0010：事件是唯一事实来源，状态是折叠结果）。
/// </para>
/// </remarks>
public sealed record VoteCountedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几次提名。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>计票时的最终投票者（投赞成者），按席位号升序。</summary>
    public required IReadOnlyList<SeatId> Voters { get; init; }

    /// <summary>其中在计票时已经死亡、因此消耗了「死后仅一次」投票权的席位。</summary>
    public IReadOnlyList<SeatId> SpentVoteTokens { get; init; } = [];

    /// <summary>计票后的「即将被处决」者；null = 当前没有人。</summary>
    public SeatId? AboutToBeExecuted { get; init; }
}
