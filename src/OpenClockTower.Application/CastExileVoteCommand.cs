namespace OpenClockTower.Application;

/// <summary>玩家在当前开放的流放提议上举手 / 放下（投票者由凭据推导；R-0044 第 4 条）。</summary>
public sealed record CastExileVoteCommand : GameCommand
{
    /// <summary>针对当天第几条流放；与当前开放的那一条不一致会被拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>true = 举手赞成；false = 放下。</summary>
    public required bool Voted { get; init; }
}
