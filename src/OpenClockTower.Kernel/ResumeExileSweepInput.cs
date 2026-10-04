namespace OpenClockTower.Kernel;

/// <summary>说书人继续中断的流放收票（重新起倒计时；R-0017 第 7 条）。</summary>
public sealed record ResumeExileSweepInput : StepMachineInput
{
    /// <summary>针对当天第几条流放；与当前开放的那一条不一致即拒绝。</summary>
    public required int ExileIndex { get; init; }
}
