namespace OpenClockTower.Kernel;

/// <summary>某席位在当前开放的提名上设置 / 撤回自己的赞成票。</summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0017：在线投票窗口内可改票，
/// 计票时才以票面快照为准；本事件记录的是"最新意愿"，最终票数由计票事件固定。
/// </remarks>
public sealed record VoteCastEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>投的是当天第几次提名。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>投票 / 改票的席位。</summary>
    public required SeatId Voter { get; init; }

    /// <summary>true = 投赞成（举手）；false = 撤回。</summary>
    public required bool Voted { get; init; }
}
