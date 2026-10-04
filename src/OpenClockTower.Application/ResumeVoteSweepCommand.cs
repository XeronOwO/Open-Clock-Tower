namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主继续中断的钟盘收票（重新起倒计时；R-0017 目标形态）。</summary>
public sealed record ResumeVoteSweepCommand : GameCommand
{
    /// <summary>对当天第几次提名继续收票；与当前开放的那一项不一致会被拒绝。</summary>
    public required int NominationIndex { get; init; }
}
