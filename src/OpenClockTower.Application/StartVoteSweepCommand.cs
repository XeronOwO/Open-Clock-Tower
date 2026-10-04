namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主开始钟盘收票（带节奏参数；R-0017 目标形态）。</summary>
public sealed record StartVoteSweepCommand : GameCommand
{
    /// <summary>对当天第几次提名开始收票；与当前开放的那一项不一致会被拒绝。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数，判定不读）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数，判定不读）。</summary>
    public required int IntervalMilliseconds { get; init; }
}
