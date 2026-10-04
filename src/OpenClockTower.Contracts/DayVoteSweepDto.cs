namespace OpenClockTower.Contracts;

/// <summary>钟盘收票的公开呈现（R-0017 目标形态）：相位 / 当前席位 / 已收席位 / 参数与剩余时间。</summary>
/// <remarks>
/// 全体玩家同一份公开事实；剩余时间是服务端读取时算出的呈现量，客户端只做动画、不驱动推进。
/// </remarks>
public sealed record DayVoteSweepDto
{
    /// <summary>Countdown（倒计时）/ Collecting（旋转收票）/ Interrupted（中断待继续）/ AwaitingCount（收完待计票）。</summary>
    public required string Phase { get; init; }

    /// <summary>分针当前指向的席位（下一待收）；倒计时 / 收完 / 中断时为 null。</summary>
    public int? CurrentSeat { get; init; }

    /// <summary>已收票的席位号，按收票顺序（字段名避开玩家投影禁词 Seats）。</summary>
    public required int[] Collected { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数，判定不读）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数，判定不读）。</summary>
    public required int IntervalMilliseconds { get; init; }

    /// <summary>距离下一拍（倒计时结束 / 下一席到点）的毫秒数（读取时算出）；中断 / 收完时为 null。</summary>
    public long? NextBeatMilliseconds { get; init; }
}
