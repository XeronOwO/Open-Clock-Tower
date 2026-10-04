namespace OpenClockTower.Kernel;

/// <summary>当天打开的额外提名窗口：授予哪个席位、是否已被用掉（R-0050）。</summary>
/// <remarks>
/// 窗口只在当天首次处决事实产出后、由规则层来源判定存在可用屠夫时打开
/// （<see cref="ExtraNominationMachine"/>）；没有窗口 = 从来没有可用屠夫 / 当次没有开窗。
/// 每个白天至多打开一次：窗口被用掉或第二次关闭后都不再开第二个窗口（R-0050 第 2 条）。
/// </remarks>
public sealed record ExtraNominationWindow
{
    /// <summary>窗口授予的席位（屠夫）：只能由本人发起额外提名。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>窗口状态。</summary>
    public required ExtraNominationWindowStatus Status { get; init; }
}
