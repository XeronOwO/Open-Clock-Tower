namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主继续中断的流放收票（重新起倒计时；R-0017 第 7 条）。</summary>
public sealed record ResumeExileSweepCommand : GameCommand
{
    /// <summary>对当天第几条流放继续收票；与当前开放的那一条不一致会被拒绝。</summary>
    public required int ExileIndex { get; init; }
}
