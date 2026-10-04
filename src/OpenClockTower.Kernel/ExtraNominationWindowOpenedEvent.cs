namespace OpenClockTower.Kernel;

/// <summary>当天首次处决后打开了额外提名窗口（百科《屠夫》；R-0050；折进当天账）。</summary>
/// <remarks>
/// 产出本事件时白天保持 Open（<see cref="DayMachine.CloseDay"/> 不产出 <see cref="DayClosedEvent"/>）：
/// 白天计划停在同一槽位，等待窗口授予席位发起额外提名或说书人直接关闭。
/// </remarks>
public sealed record ExtraNominationWindowOpenedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>窗口授予的席位（屠夫）。</summary>
    public required SeatId Seat { get; init; }
}
