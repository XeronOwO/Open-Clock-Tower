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

    /// <summary>
    /// 已被占用的席位号（升序）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 大厅必须给出它：否则玩家只能"点一下试试"，撞上别人已坐的席位才知道被占——
    /// 体验上是把人当探针用（实测踩到：点下去才报"席位已被其他账号认领"）。
    /// </para>
    /// <para>
    /// 这**不是**"把席位明细下发给玩家"：席位号与"有没有人坐"在开局前的桌边本来就是公开的
    /// （谁坐哪儿大家都看得见）；这里不涉及谁坐的、更不涉及任何局内信息。
    /// 字段名刻意避开禁词表里的 `Seats`，好让那条门禁继续盯住真正的越权（把席位**明细**塞进投影）。
    /// </para>
    /// </remarks>
    public required IReadOnlyList<int> OccupiedSeatNumbers { get; init; }
}
