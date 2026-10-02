using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一次房间重建的结果：两个派生视图（步骤机 / 状态账）与三项"重建前是否一致"的结论。
/// </summary>
/// <remarks>
/// 重建报告（<see cref="RoomRebuildReport"/>）从它派生；分开是为了让"读事件 → 折叠 → 对比 → 写快照"
/// 这条纯编排留在 <see cref="RoomRebuildService"/>，会话只负责把结果落回自己的状态位（D-0014 能力 3）。
/// </remarks>
public sealed record RoomRebuildOutcome
{
    /// <summary>重建后的步骤机状态；空事件流为 null。</summary>
    public required StepMachineState? Machine { get; init; }

    /// <summary>重建后的状态账（五账）。</summary>
    public required GameState State { get; init; }

    /// <summary>重建所依据的全量事件（已按序号升序）。</summary>
    public required IReadOnlyList<StoredEvent> Events { get; init; }

    /// <summary>重建对应的事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>重建结果与重建前内存步骤机状态是否一致。</summary>
    public required bool MachineEquivalent { get; init; }

    /// <summary>重建结果与持久化快照是否一致；无快照时为 null。</summary>
    public required bool? SnapshotEquivalent { get; init; }

    /// <summary>重建结果与重建前内存状态账是否一致。</summary>
    public required bool LedgerEquivalent { get; init; }
}
