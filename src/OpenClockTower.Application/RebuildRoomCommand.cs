namespace OpenClockTower.Application;

/// <summary>
/// 说书人 / 宿主按事件日志重建房间状态（D-0014 能力 3：数据异常时的恢复）。
/// </summary>
/// <remarks>
/// 重建失败必须**显式报错、拒绝静默继续**；重建不删除事件，只重算状态与快照。
/// </remarks>
public sealed record RebuildRoomCommand : GameCommand
{
    /// <summary>重建原因。</summary>
    public required string Reason { get; init; }
}
