using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的席位绑定表（D-0021）。</summary>
public sealed class EfSeatBindingStore : ISeatBindingStore
{
    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造绑定表。</summary>
    public EfSeatBindingStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.SeatBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.GameId == gameId.Value && item.Seat == seat.Value,
                cancellationToken);
        return row is null ? null : ToBinding(row);
    }

    /// <inheritdoc />
    public async Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.SeatBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.GameId == gameId.Value && item.AccountId == accountId.Value,
                cancellationToken);
        return row is null ? null : ToBinding(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SeatBindings
            .AsNoTracking()
            .Where(item => item.GameId == gameId.Value)
            .OrderBy(item => item.Seat)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToBinding)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
        IReadOnlyCollection<GameId> gameIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gameIds);
        if (gameIds.Count == 0)
        {
            // 一桌都没有时不该退化成 `WHERE 1=0` 之外的东西，更不该白开一次连接。
            return [];
        }

        var ids = gameIds.Select(id => id.Value).ToArray();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SeatBindings
            .AsNoTracking()
            .Where(item => ids.Contains(item.GameId))
            .OrderBy(item => item.GameId)
            .ThenBy(item => item.Seat)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToBinding)];
    }

    /// <inheritdoc />
    public async Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        db.SeatBindings.Add(new SeatBindingEntity
        {
            GameId = binding.GameId.Value,
            Seat = binding.Seat.Value,
            AccountId = binding.AccountId.Value,
            BoundAt = binding.BoundAt,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // 唯一约束挡下的竞态：确认是"席位或账号已被占用"才按业务拒绝，其他数据库错误原样抛。
            await using var verify = await _factory.CreateDbContextAsync(cancellationToken);
            var occupied = await verify.SeatBindings
                .AsNoTracking()
                .AnyAsync(
                    item => item.GameId == binding.GameId.Value
                            && (item.Seat == binding.Seat.Value || item.AccountId == binding.AccountId.Value),
                    cancellationToken);
            if (occupied)
            {
                return false;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.SeatBindings.FirstOrDefaultAsync(
            item => item.GameId == gameId.Value && item.Seat == seat.Value,
            cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.SeatBindings.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static SeatBinding ToBinding(SeatBindingEntity row) => new()
    {
        GameId = new GameId(row.GameId),
        Seat = new SeatId(row.Seat),
        AccountId = new AccountId(row.AccountId),
        BoundAt = row.BoundAt,
    };
}
