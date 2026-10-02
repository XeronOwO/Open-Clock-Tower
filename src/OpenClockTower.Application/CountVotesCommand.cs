namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主对当前开放的提名计票（票面快照冻结，R-0017）。</summary>
public sealed record CountVotesCommand : GameCommand
{
    /// <summary>对当天第几次提名计票；与当前开放的那一项不一致会被拒绝。</summary>
    public required int NominationIndex { get; init; }
}
