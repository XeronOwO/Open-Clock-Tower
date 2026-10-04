namespace OpenClockTower.Contracts;

/// <summary>白天一次提名的公开账目（提名与投票本身就是桌面上的公开信息）。</summary>
public sealed record DayNominationDto
{
    /// <summary>当天第几次提名（从 1 起）。</summary>
    public required int Index { get; init; }

    /// <summary>发起提名的席位。</summary>
    public required int Nominator { get; init; }

    /// <summary>被提名的席位。</summary>
    public required int Nominee { get; init; }

    /// <summary>Voting（投票窗口开着）/ Counted（已计票）。</summary>
    public required string Status { get; init; }

    /// <summary>当前 / 最终票数。</summary>
    public required int Votes { get; init; }

    /// <summary>当前 / 最终投赞成者的席位（按席位号升序）：钟盘形态下 = 已收票的冻结结论。</summary>
    public required int[] Voters { get; init; }

    /// <summary>当前举着手（赞成）的席位，按席位号升序；钟盘形态的公开面（线下所有人都看得见）。</summary>
    public required int[] HandsRaised { get; init; }

    /// <summary>钟盘收票呈现；这项提名没在收票（还没点「开始」/ 旧日志 / 已计票）时为 null。</summary>
    public DayVoteSweepDto? Sweep { get; init; }
}
