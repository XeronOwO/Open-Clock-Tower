namespace OpenClockTower.Kernel;

/// <summary>
/// 当天一条流放提议的账目：谁提议谁、表决状态、票面与钟盘收票状态、计票结论。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《旅行者》· 2026-10-04 抓取 与 <c>docs/standard/rulings.md</c> R-0044：任意在局玩家
/// （含死者）可发起；目标必须是在局旅行者；每名旅行者每个白天至多被提议一次（成败都算）；
/// 同日可多次、顺序进行（同一天至多一条未结清）。
/// </para>
/// <para>
/// 票面的折叠口径与 <see cref="NominationRecord"/> 的钟盘形态完全一致（R-0017）：
/// <see cref="Sweep"/> 不为 null 时，<see cref="HandsRaised"/> 记"现在谁举着手"，
/// <see cref="Ballot"/> 只由逐席收票追加冻结结论，收票完成后即最终投票者名单。
/// </para>
/// </remarks>
public sealed record ExileRecord
{
    /// <summary>当天第几条流放（从 1 起，进事件流后稳定）。</summary>
    public required int Index { get; init; }

    /// <summary>发起提议的席位（在局玩家均可，含死者；R-0044 第 2 条）。</summary>
    public required SeatId Proposer { get; init; }

    /// <summary>被提议流放的席位（在局旅行者）。</summary>
    public required SeatId Target { get; init; }

    /// <summary>表决状态。</summary>
    public required ExileStatus Status { get; init; }

    /// <summary>逐席冻结的赞成票（按席位号升序）；计票后即最终名单。</summary>
    public IReadOnlyList<SeatId> Ballot { get; init; } = [];

    /// <summary>钟盘形态下"现在举着手"的席位（按席位号升序）。</summary>
    public IReadOnlyList<SeatId> HandsRaised { get; init; } = [];

    /// <summary>钟盘收票状态；还没有点「开始」时为 null。</summary>
    public VoteSweepState? Sweep { get; init; }

    /// <summary>计票结论；还没计票时为 null。</summary>
    public ExileConclusion? Conclusion { get; init; }
}
