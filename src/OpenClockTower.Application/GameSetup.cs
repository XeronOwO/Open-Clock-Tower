namespace OpenClockTower.Application;

/// <summary>一局的会话信息：谁拿着哪张票据（服务端持久化，重启后仍有效）+ 大厅元数据。</summary>
/// <remarks>
/// 大厅元数据（<see cref="Name"/> / <see cref="IsLocked"/>）是**会话信息**，不进事件流：
/// 它不影响任何规则判定，也不该出现在复盘里（D-0015：状态账只记事实与归因）。
/// </remarks>
public sealed record GameSetup
{
    /// <summary>游戏标识。</summary>
    public required GameId GameId { get; init; }

    /// <summary>各席位的票据。</summary>
    public required IReadOnlyList<SeatTicket> Seats { get; init; }

    /// <summary>说书人票据。</summary>
    public required string StorytellerTicket { get; init; }

    /// <summary>桌名（玩家在大厅里看到的标题；未命名为空串）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>是否锁定：锁定后不再接受新的入座（已在座的玩家不受影响，D-0025）。</summary>
    public bool IsLocked { get; init; }
}
