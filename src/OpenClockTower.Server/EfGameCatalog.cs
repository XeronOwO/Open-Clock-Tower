using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的会话票据目录（多桌：每桌一行，D-0024）。</summary>
/// <remarks>
/// 建桌时刻在**插入**那一支写一次（<see cref="GameSetupEntity.CreatedAt"/>）：它是这一行的事实，
/// 由持久化层拿时钟盖一次章最省事，也不必让"建桌"这个领域动作多背一个时间参数。
/// 更新（改名 / 锁桌 / 重存票据）一律不碰它——否则一桌改一次名就不会到期。
/// </remarks>
public sealed class EfGameCatalog : IGameCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<GameDbContext> _factory;
    private readonly IClock _clock;

    /// <summary>构造目录。</summary>
    public EfGameCatalog(IDbContextFactory<GameDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<GameSetup?> FindAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.GameId == gameId.Value, cancellationToken);

        return row is null ? null : ToSetup(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(GameSetup setup, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Games.FirstOrDefaultAsync(item => item.GameId == setup.GameId.Value, cancellationToken);
        var seatsJson = JsonSerializer.Serialize(setup.Seats, Options);

        if (row is null)
        {
            db.Games.Add(new GameSetupEntity
            {
                GameId = setup.GameId.Value,
                SeatsJson = seatsJson,
                CreatedByAccountId = setup.CreatedByAccountId?.Value,
                Name = setup.Name,
                IsLocked = setup.IsLocked,
                CreatedAt = _clock.UtcNow,
            });
        }
        else
        {
            row.SeatsJson = seatsJson;
            row.CreatedByAccountId = setup.CreatedByAccountId?.Value;
            row.Name = setup.Name;
            row.IsLocked = setup.IsLocked;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameSetup>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Games
            .AsNoTracking()
            .OrderBy(item => item.GameId)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToSetup)];
    }

    /// <inheritdoc />
    public async Task UpdateLobbyAsync(
        GameId gameId,
        string name,
        bool isLocked,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Games.FirstOrDefaultAsync(item => item.GameId == gameId.Value, cancellationToken);
        if (row is null)
        {
            // 未知的桌不该被这条更新凭空造出来：那是建桌用例的职责。
            throw new InvalidOperationException($"更新桌元数据失败：桌不存在 game={gameId.Value}");
        }

        row.Name = name;
        row.IsLocked = isLocked;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static GameSetup ToSetup(GameSetupEntity row) => new()
    {
        GameId = new GameId(row.GameId),
        Seats = JsonSerializer.Deserialize<SeatTicket[]>(row.SeatsJson, Options) ?? [],
        CreatedByAccountId = row.CreatedByAccountId is { } owner ? new AccountId(owner) : null,
        Name = row.Name,
        IsLocked = row.IsLocked,
    };
}
