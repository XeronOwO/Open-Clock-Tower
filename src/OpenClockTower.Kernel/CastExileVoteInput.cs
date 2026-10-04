namespace OpenClockTower.Kernel;

/// <summary>某席位在当前开放的流放提议上举手 / 放下（R-0044 第 4 条）。</summary>
public sealed record CastExileVoteInput : StepMachineInput
{
    /// <summary>举手 / 放下的席位。</summary>
    public required SeatId Voter { get; init; }

    /// <summary>针对当天第几条流放；与当前开放的那一条不一致即拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>true = 举手赞成；false = 放下。</summary>
    public required bool Voted { get; init; }
}
