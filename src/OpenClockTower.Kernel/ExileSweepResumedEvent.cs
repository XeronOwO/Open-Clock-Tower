namespace OpenClockTower.Kernel;

/// <summary>说书人继续中断的流放收票（重新起倒计时，从下一未收席位接着收；R-0017 第 7 条）。</summary>
public sealed record ExileSweepResumedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几条流放。</summary>
    public required int ExileIndex { get; init; }
}
