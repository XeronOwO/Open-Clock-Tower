namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主在流放收票全部完成后计票（票面 = 逐席冻结结论；R-0044 第 5 / 9 条）。</summary>
public sealed record CountExileVotesCommand : GameCommand
{
    /// <summary>对当天第几条流放计票；与当前开放的那一条不一致会被拒绝。</summary>
    public required int ExileIndex { get; init; }
}
