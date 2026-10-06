namespace OpenClockTower.Contracts;

/// <summary>
/// 大厅里的一桌（D-0025）：玩家挑桌时需要知道的东西。
/// </summary>
/// <remarks>
/// 刻意只含公开信息：桌名、人数、是否已开局、是否锁定。
/// 票据、席位归属、局内状态一律不下发——那是入座之后的事（D-0012）。
/// </remarks>
public sealed record LobbyTableDto
{
    /// <summary>桌标识（加入时用它声明 <c>?gameId=</c>）。</summary>
    public required string GameId { get; init; }

    /// <summary>桌名（未命名时为空串，前端回退成标识）。</summary>
    public required string Name { get; init; }

    /// <summary>席位数（这一桌坐得下几个人）。</summary>
    public required int SeatCapacity { get; init; }

    /// <summary>
    /// 已经坐下的人数（大厅里显示"3 / 7"）。
    /// </summary>
    /// <remarks>
    /// 刻意只给**计数**，不给席位明细：谁坐在几号席属于桌内信息，入座之后才该看到。
    /// 字段名避开 `Seats` 这类禁词，正是为了让"别把席位明细下发给玩家"这条门禁保持锋利。
    /// </remarks>
    public required int TakenSeatCount { get; init; }

    /// <summary>是否已开局（已经开过第一个夜晚或白天）。</summary>
    public required bool Started { get; init; }

    /// <summary>是否锁定（锁定后不再接受新的入座）。</summary>
    public required bool Locked { get; init; }
}
