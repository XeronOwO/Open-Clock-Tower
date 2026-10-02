namespace OpenClockTower.Kernel;

/// <summary>某玩家在当前开放的提名上投票 / 撤回（在线口径见 R-0017）。</summary>
public sealed record CastVoteInput : StepMachineInput
{
    /// <summary>投票 / 改票的席位。</summary>
    public required SeatId Voter { get; init; }

    /// <summary>针对当天第几次提名；与当前开放的那一项不一致即拒绝。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>true = 投赞成；false = 撤回。</summary>
    public required bool Voted { get; init; }
}
