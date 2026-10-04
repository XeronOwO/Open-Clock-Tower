namespace OpenClockTower.Kernel;

/// <summary>说书人对当前开放的流放计票（票面快照冻结；R-0044 第 5 / 9 条）。</summary>
public sealed record CountExileVotesInput : StepMachineInput
{
    /// <summary>对当天第几条流放计票；与当前开放的那一条不一致即拒绝。</summary>
    public required int ExileIndex { get; init; }
}
