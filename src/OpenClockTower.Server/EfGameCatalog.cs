using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>EF Core + SQLite 实现的会话目录（多桌：每桌一行，D-0024）。</summary>
/// <remarks>
/// <para>
/// 建桌时刻在**插入**那一支写一次（<see cref="GameSetupEntity.CreatedAt"/>）：它是这一行的事实，
/// 由持久化层拿时钟盖一次章最省事，也不必让"建桌"这个领域动作多背一个时间参数。
/// 更新（改名 / 换访问模式 / 存席位名单）一律不碰它——否则一桌改一次名就不会到期。
/// </para>
/// <para>
/// <b>这一列里没有凭据</b>（D-0038）：<c>SeatsJson</c> 存的是席位号数组 <c>[1,2,3]</c>，
/// 邀请码在 <c>SeatInvitations</c> 表里、且只有哈希。从前它是 <c>[{"seat":…,"ticket":"…"}]</c>
/// ——明文凭据与席位名单挤在一列，读一次名单就把凭据读进了内存与备份（审计 G-A2-2）。
/// 老库那一形态由结构 v4 的迁移抹掉。
/// </para>
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
        var seatsJson = JsonSerializer.Serialize(setup.Seats.Select(seat => seat.Value).ToArray(), Options);

        if (row is null)
        {
            db.Games.Add(new GameSetupEntity
            {
                GameId = setup.GameId.Value,
                SeatsJson = seatsJson,
                CreatedByAccountId = setup.CreatedByAccountId?.Value,
                Name = setup.Name,
                IsInviteOnly = setup.IsInviteOnly,
                CreatedAt = _clock.UtcNow,
            });
        }
        else
        {
            row.SeatsJson = seatsJson;
            row.CreatedByAccountId = setup.CreatedByAccountId?.Value;
            row.Name = setup.Name;
            row.IsInviteOnly = setup.IsInviteOnly;
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
        bool isInviteOnly,
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
        row.IsInviteOnly = isInviteOnly;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static GameSetup ToSetup(GameSetupEntity row) => new()
    {
        GameId = new GameId(row.GameId),
        Seats = [.. (JsonSerializer.Deserialize<int[]>(row.SeatsJson, Options) ?? []).Select(value => new SeatId(value))],
        CreatedByAccountId = row.CreatedByAccountId is { } owner ? new AccountId(owner) : null,
        Name = row.Name,
        IsInviteOnly = row.IsInviteOnly,
    };
}
