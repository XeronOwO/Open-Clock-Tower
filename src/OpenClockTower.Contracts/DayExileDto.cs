namespace OpenClockTower.Contracts;

/// <summary>当天一条流放提议的公开账目（流放是桌面上的公开流程，R-0044）。</summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0044：流放不是提名 / 不是投票 / 不是处决，公开面独立成账；
/// 钟盘收票的呈现与提名共用 <see cref="DayVoteSweepDto"/> 形状，外层靠本记录的
/// <see cref="Index"/> 与提名区分（票据 traveller-and-exile · D2 / D7）。
/// </remarks>
public sealed record DayExileDto
{
    /// <summary>当天第几条流放（从 1 起）。</summary>
    public required int Index { get; init; }

    /// <summary>发起提议的席位（在局玩家均可，含死者；R-0044 第 2 条）。</summary>
    public required int Proposer { get; init; }

    /// <summary>被提议流放的席位（在局旅行者）。</summary>
    public required int Target { get; init; }

    /// <summary>Voting（表决中，含收票进行中）/ Counted（已计票）。</summary>
    public required string Status { get; init; }

    /// <summary>当前 / 最终票数（= 已收票的赞成数；计票后为最终名单）。</summary>
    public required int Votes { get; init; }

    /// <summary>当前 / 最终投赞成者的席位（按席位号升序）：钟盘形态下 = 已收票的冻结结论。</summary>
    public required int[] Voters { get; init; }

    /// <summary>当前举着手（赞成）的席位，按席位号升序；钟盘形态的公开面（R-0044 第 10 条）。</summary>
    public required int[] HandsRaised { get; init; }

    /// <summary>钟盘收票呈现；这条流放没在收票（还没点「开始」/ 已计票）时为 null。</summary>
    public DayVoteSweepDto? Sweep { get; init; }

    /// <summary>
    /// 计票结论：Exiled（流放成立、目标死亡）/ Protected（达线但受死亡保护）/ VotesInsufficient
    /// （未达线）；还没计票时为 null。口径见 R-0044 / R-0048。
    /// </summary>
    public string? Conclusion { get; init; }
}
