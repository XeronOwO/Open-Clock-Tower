namespace OpenClockTower.Kernel;

/// <summary>说书人开始流放表决的钟盘收票（带节奏参数；R-0017 机制，R-0044 语义）。</summary>
public sealed record StartExileSweepInput : StepMachineInput
{
    /// <summary>针对当天第几条流放；与当前开放的那一条不一致即拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数，判定不读）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数，判定不读）。</summary>
    public required int IntervalMilliseconds { get; init; }
}
