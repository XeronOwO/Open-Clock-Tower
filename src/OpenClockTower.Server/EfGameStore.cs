using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现：事件 + 快照 + 回执在一个事务里提交。</summary>
public sealed class EfGameStore : IGameStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造存储。</summary>
    public EfGameStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEvent>> ReadEventsAsync(
        GameId gameId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Events
            .AsNoTracking()
            .Where(row => row.GameId == gameId.Value && row.Sequence > afterSequence)
            .OrderBy(row => row.Sequence)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new StoredEvent
            {
                Sequence = row.Sequence,
                Event = GameEventSerialization.Deserialize(row.Type, row.Payload),
                RecordedAt = row.RecordedAt,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<long> FindLastSequenceAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var last = await db.Events
            .AsNoTracking()
            .Where(row => row.GameId == gameId.Value)
            .Select(row => (long?)row.Sequence)
            .MaxAsync(cancellationToken);
        return last ?? 0;
    }

    /// <inheritdoc />
    public async Task<StoredSnapshot?> FindSnapshotAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Snapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.GameId == gameId.Value, cancellationToken);

        return row is null
            ? null
            : new StoredSnapshot
            {
                Sequence = row.Sequence,
                Machine = GameEventSerialization.DeserializeState(row.MachineJson),
                RecordedAt = row.RecordedAt,
            };
    }

    /// <inheritdoc />
    public async Task<CommandReceipt?> FindReceiptAsync(
        GameId gameId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Receipts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.GameId == gameId.Value && item.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return row is null
            ? null
            : new CommandReceipt
            {
                IdempotencyKey = row.IdempotencyKey,
                FirstSequence = row.FirstSequence,
                LastSequence = row.LastSequence,
            };
    }

    /// <inheritdoc />
    public async Task CommitAsync(GameCommit commit, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var draft in commit.Events)
        {
            db.Events.Add(new EventEntity
            {
                GameId = commit.GameId.Value,
                Sequence = draft.Sequence,
                Type = GameEventSerialization.TypeNameOf(draft.Event),
                Payload = GameEventSerialization.Serialize(draft.Event),
                RecordedAt = draft.RecordedAt,
            });
        }

        if (commit.Receipt is { } receipt)
        {
            db.Receipts.Add(new ReceiptEntity
            {
                GameId = commit.GameId.Value,
                IdempotencyKey = receipt.IdempotencyKey,
                FirstSequence = receipt.FirstSequence,
                LastSequence = receipt.LastSequence,
            });
        }

        var machineJson = GameEventSerialization.SerializeState(commit.Snapshot.Machine);
        var snapshot = await db.Snapshots
            .FirstOrDefaultAsync(item => item.GameId == commit.GameId.Value, cancellationToken);
        if (snapshot is null)
        {
            db.Snapshots.Add(new SnapshotEntity
            {
                GameId = commit.GameId.Value,
                Sequence = commit.Snapshot.Sequence,
                MachineJson = machineJson,
                RecordedAt = commit.Snapshot.RecordedAt,
            });
        }
        else
        {
            snapshot.Sequence = commit.Snapshot.Sequence;
            snapshot.MachineJson = machineJson;
            snapshot.RecordedAt = commit.Snapshot.RecordedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
