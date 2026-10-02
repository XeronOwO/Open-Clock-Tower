namespace OpenClockTower.Kernel;

/// <summary>说书人对当前开放的提名计票（票面快照冻结，R-0017）。</summary>
public sealed record CountVotesInput : StepMachineInput
{
    /// <summary>对当天第几次提名计票；与当前开放的那一项不一致即拒绝。</summary>
    public required int NominationIndex { get; init; }
}
