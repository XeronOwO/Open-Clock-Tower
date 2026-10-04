namespace OpenClockTower.Kernel;

/// <summary>说书人继续中断的钟盘收票（重新起倒计时；R-0017 目标形态）。</summary>
public sealed record ResumeVoteSweepInput : StepMachineInput
{
    /// <summary>针对当天第几次提名；与当前开放的那一项不一致即拒绝。</summary>
    public required int NominationIndex { get; init; }
}
