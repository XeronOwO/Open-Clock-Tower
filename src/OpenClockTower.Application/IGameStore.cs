namespace OpenClockTower.Application;

/// <summary>
/// 事件流 / 快照 / 回执的持久化端口（由 Server 用 EF Core + SQLite 实现）。
/// </summary>
/// <remarks>
/// 事件是唯一事实来源（D-0010）；快照与回执都是派生数据，必须与事件同事务提交。
/// </remarks>
public interface IGameStore
{
    /// <summary>读取序号大于 <paramref name="afterSequence"/> 的全部事件，按序号升序。</summary>
    Task<IReadOnlyList<StoredEvent>> ReadEventsAsync(
        GameId gameId,
        long afterSequence,
        CancellationToken cancellationToken);

    /// <summary>读取最新事件序号（不做反序列化；事件载荷损坏时仍能拿到"写到哪了"）。</summary>
    Task<long> FindLastSequenceAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>读取最新快照；没有时为 null。</summary>
    Task<StoredSnapshot?> FindSnapshotAsync(GameId gameId, CancellationToken cancellationToken);

    /// <summary>按幂等键查找回执；没有时为 null。</summary>
    Task<CommandReceipt?> FindReceiptAsync(
        GameId gameId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>原子提交一次事件 + 快照 + 回执。</summary>
    Task CommitAsync(GameCommit commit, CancellationToken cancellationToken);
}
