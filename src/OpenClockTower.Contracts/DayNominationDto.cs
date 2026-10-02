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

    /// <summary>当前 / 最终投赞成者的席位（按席位号升序）。</summary>
    public required int[] Voters { get; init; }
}
