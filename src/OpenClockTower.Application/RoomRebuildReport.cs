namespace OpenClockTower.Application;

/// <summary>
/// 房间重建报告：把"重建前后是否一致"明确回给说书人（D-0014 能力 3）。
/// </summary>
/// <remarks>
/// 只写日志不算交付：说书人端必须看得见"内存状态 / 派生快照与事件流是否一致"，
/// 才能决定要不要继续兜底。
/// </remarks>
public sealed record RoomRebuildReport
{
    /// <summary>重建结果与重建前内存状态是否一致。</summary>
    public required bool MachineEquivalent { get; init; }

    /// <summary>重建结果与持久化快照是否一致；无快照时为 null。</summary>
    public bool? SnapshotEquivalent { get; init; }

    /// <summary>
    /// 重建结果与内存状态账（<c>GameState</c> 五账）是否一致。
    /// 步骤机一致不等于账一致：两者是同一条事件流上的两个派生视图（D-0010），必须分开比。
    /// </summary>
    public required bool LedgerEquivalent { get; init; }

    /// <summary>重建对应的事件序号。</summary>
    public required long Sequence { get; init; }
}
