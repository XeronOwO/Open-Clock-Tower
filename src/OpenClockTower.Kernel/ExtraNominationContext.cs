namespace OpenClockTower.Kernel;

/// <summary>规则层判定「有没有可用屠夫」的输入（R-0050）：只读账、座次与当天账。</summary>
public sealed record ExtraNominationContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局在局座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>当天账（窗口打开前）；内核夹具或异常路径可能为 null。</summary>
    public DayRecord? Day { get; init; }

    /// <summary>刚刚发生的当次处决席位（当天首次处决；R-0050 第 1 条）。</summary>
    public required SeatId ExecutedSeat { get; init; }
}
