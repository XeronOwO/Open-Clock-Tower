using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的会话票据目录。</summary>
public sealed class EfGameCatalog : IGameCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造目录。</summary>
    public EfGameCatalog(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<GameSetup?> FindAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.GameId == gameId.Value, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var seats = JsonSerializer.Deserialize<SeatTicket[]>(row.SeatsJson, Options) ?? [];
        return new GameSetup
        {
            GameId = gameId,
            Seats = seats,
            StorytellerTicket = row.StorytellerTicket,
        };
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
                StorytellerTicket = setup.StorytellerTicket,
            });
        }
        else
        {
            row.SeatsJson = seatsJson;
            row.StorytellerTicket = setup.StorytellerTicket;
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

        return
        [
            .. rows.Select(row => new GameSetup
            {
                GameId = new GameId(row.GameId),
                Seats = JsonSerializer.Deserialize<SeatTicket[]>(row.SeatsJson, Options) ?? [],
                StorytellerTicket = row.StorytellerTicket,
            }),
        ];
    }
}
