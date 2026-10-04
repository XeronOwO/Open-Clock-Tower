namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主开始流放表决的钟盘收票（带节奏参数；R-0044 第 10 条沿用 R-0017）。</summary>
public sealed record StartExileSweepCommand : GameCommand
{
    /// <summary>对当天第几条流放开始收票；与当前开放的那一条不一致会被拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数，判定不读）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数，判定不读）。</summary>
    public required int IntervalMilliseconds { get; init; }
}
