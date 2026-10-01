namespace OpenClockTower.Kernel;

/// <summary>说书人接管：暂停自动推进，改为手动逐步驱动（D-0014）。</summary>
public sealed record TakeOverInput : StepMachineInput
{
    /// <summary>接管原因。</summary>
    public required string Reason { get; init; }
}
