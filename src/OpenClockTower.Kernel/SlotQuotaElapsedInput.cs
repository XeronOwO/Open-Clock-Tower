namespace OpenClockTower.Kernel;

/// <summary>当前槽位的最短配额已走完（由宿主节拍器按服务端时钟判定后送入）。</summary>
public sealed record SlotQuotaElapsedInput : StepMachineInput;
