using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 按事件日志重建房间的编排步骤：读全量事件 → 折叠两个派生视图 → 对比 → 写入新快照 + 回执。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameSession"/> 拆出：会话是"状态持有者 + 用例调度"，而这里是重建这一条用例的
/// 完整算法（D-0014 能力 3）。失败仍以异常抛出，由会话决定"降级位置位 / 回执怎么写"——
/// 状态位的所有权不搬走（状态属于所有者）。
/// </para>
/// <para>
/// 折叠必须**同源**：同一条事件流上步骤机与状态账是两个派生视图（D-0010），一起重算才不会留下陈旧账。
/// </para>
/// </remarks>
public static class RoomRebuildService
{
    /// <summary>执行重建并返回结果；事件流不可读 / 损坏时抛出（由调用方显式报错，不静默继续）。</summary>
    public static async Task<RoomRebuildOutcome> RebuildAsync(
        GameId gameId,
        IGameStore store,
        StepMachineState? machine,
        GameState state,
        string idempotencyKey,
        DateTimeOffset recordedAt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(logger);

        var storedEvents = await store.ReadEventsAsync(gameId, afterSequence: 0, cancellationToken);
        var eventStream = storedEvents.Select(item => item.Event).ToArray();
        var rebuilt = eventStream.Length == 0 ? null : StepMachine.Fold(eventStream);

        // 状态账与步骤机同源折叠：同一条事件流，两个派生视图必须一起重算，否则重建后账会陈旧。
        var rebuiltState = GameStateMachine.Fold(eventStream);
        var machineEquivalent = StepMachineStateComparer.AreEquivalent(machine, rebuilt);
        var ledgerEquivalent = GameStateComparer.AreEquivalent(state, rebuiltState);

        bool? snapshotEquivalent;
        try
        {
            var snapshot = await store.FindSnapshotAsync(gameId, cancellationToken);
            snapshotEquivalent = snapshot is null
                ? null
                : StepMachineStateComparer.AreEquivalent(snapshot.Machine, rebuilt);
        }
        catch (InvalidOperationException exception)
        {
            // 旧快照读不出来 = 与事件流不一致；快照是派生数据，重建就是来修它的。
            logger.LogWarning(exception, "读取旧快照失败，按不一致处理：game={GameId}", gameId);
            snapshotEquivalent = false;
        }

        var lastSequence = storedEvents.Count == 0 ? 0 : storedEvents[^1].Sequence;
        await store.CommitAsync(
            new GameCommit
            {
                GameId = gameId,
                Events = [],
                Snapshot = new StoredSnapshot
                {
                    Sequence = lastSequence,
                    Machine = rebuilt,
                    RecordedAt = recordedAt,
                },
                Receipt = new CommandReceipt
                {
                    IdempotencyKey = idempotencyKey,
                    FirstSequence = lastSequence + 1,
                    LastSequence = lastSequence,
                },
            },
            cancellationToken);

        return new RoomRebuildOutcome
        {
            Machine = rebuilt,
            State = rebuiltState,
            Events = storedEvents,
            Sequence = lastSequence,
            MachineEquivalent = machineEquivalent,
            SnapshotEquivalent = snapshotEquivalent,
            LedgerEquivalent = ledgerEquivalent,
        };
    }
}
