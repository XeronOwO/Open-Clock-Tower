using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>钟盘收票的呈现快照（R-0017 目标形态）：相位、当前席位、已收席位与剩余时间。</summary>
/// <remarks>
/// 剩余时间是**读取时**由应用层时钟算出的呈现量（与 <see cref="PendingRequestSummary.Waiting"/> 同姿态），
/// 不构成新事实；客户端只做动画，不驱动推进（D-0013 §6）。
/// </remarks>
public sealed record VoteSweepView
{
    /// <summary>相位：Countdown / Collecting / Interrupted / AwaitingCount。</summary>
    public required string Phase { get; init; }

    /// <summary>分针当前指向的席位（下一待收）；倒计时 / 收完 / 中断时为 null。</summary>
    public int? CurrentSeat { get; init; }

    /// <summary>已收票的席位，按收票顺序。</summary>
    public required IReadOnlyList<SeatId> Collected { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数）。</summary>
    public required int IntervalMilliseconds { get; init; }

    /// <summary>距离下一拍的毫秒数（读取时算出）；中断 / 收完 / 无锚点时为 null。</summary>
    public long? NextBeatMilliseconds { get; init; }
}
