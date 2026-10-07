using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// EF Core + SQLite 实现的桌退役端口（M5 / G-A6-5）：活跃度读数与五表联删。
/// </summary>
/// <remarks>
/// <para>
/// **读数固定三条查询**（桌 / 事件按桌聚合 / 绑定按桌聚合），不随桌数增长：审计 G-A5-5 那条
/// N+1 就是"逐桌查一次"长出来的，没理由在回收这条路上再写一遍。
/// </para>
/// <para>
/// 聚合在 SQL 里做（<c>GROUP BY</c>），不把事件行拉进内存：一张打了三天的桌有上万条事件，
/// "算个最后时刻"不该把它们全部读出来 —— 而客户端的强制聚合正是那样（EF 的 SQLite 提供程序
/// **不支持对 <c>DateTimeOffset</c> 求 <c>MAX</c>**，实测报
/// <c>SQLite cannot apply aggregate operator 'Max' on expressions of type 'DateTimeOffset'</c>）。
/// 于是那两条聚合走原生 SQL，时刻以库里的 TEXT 形态取回来再解析。
/// </para>
/// <para>
/// 时刻列存的是 EF 的 SQLite 形态（TEXT，写库的是 <c>SystemClock</c> 的 UTC，偏移量恒为 <c>+00:00</c>），
/// 因此 SQL 里 <c>MAX</c> 的字符串序与时间序一致——这条前提由 <see cref="SystemClock"/> 唯一时钟保证，
/// 换时钟（带本地偏移）就会悄悄改变这里的语义。
/// </para>
/// </remarks>
public sealed class EfTableRetirementStore : ITableRetirementStore
{
    /// <summary>事件按桌聚合：条数 / 最后记录时刻 / 载荷字节（按字节量，中文一个字三字节）。</summary>
    private const string EventAggregateSql =
        """
        SELECT "GameId" AS "GameId",
               COUNT(*) AS "EventCount",
               MAX("RecordedAt") AS "LastRecordedAt",
               COALESCE(SUM(LENGTH(CAST("Payload" AS BLOB))), 0) AS "PayloadBytes"
        FROM "Events"
        GROUP BY "GameId"
        """;

    /// <summary>席位绑定按桌聚合：最后一次认领的时刻。</summary>
    private const string BindingAggregateSql =
        """
        SELECT "GameId" AS "GameId",
               MAX("BoundAt") AS "LastBoundAt"
        FROM "SeatBindings"
        GROUP BY "GameId"
        """;

    private readonly IDbContextFactory<GameDbContext> _factory;

    /// <summary>构造存储。</summary>
    public EfTableRetirementStore(IDbContextFactory<GameDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TableActivity>> ListActivityAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var games = await db.Games
            .AsNoTracking()
            .OrderBy(row => row.GameId)
            .Select(row => new { row.GameId, row.Name, row.CreatedAt })
            .ToListAsync(cancellationToken);

        var events = await db.Database
            .SqlQueryRaw<EventAggregate>(EventAggregateSql)
            .ToListAsync(cancellationToken);
        var bindings = await db.Database
            .SqlQueryRaw<BindingAggregate>(BindingAggregateSql)
            .ToListAsync(cancellationToken);

        var byGame = events.ToDictionary(row => row.GameId, StringComparer.Ordinal);
        var bindingByGame = bindings.ToDictionary(row => row.GameId, StringComparer.Ordinal);

        return
        [
            .. games.Select(game =>
            {
                var eventsOfGame = byGame.GetValueOrDefault(game.GameId);
                return new TableActivity(
                    new GameId(game.GameId),
                    game.Name,
                    game.CreatedAt,
                    eventsOfGame?.EventCount ?? 0,
                    Parse(eventsOfGame?.LastRecordedAt),
                    Parse(bindingByGame.GetValueOrDefault(game.GameId)?.LastBoundAt),
                    eventsOfGame?.PayloadBytes ?? 0);
            }),
        ];
    }

    /// <inheritdoc />
    public async Task<TablePurgeResult> PurgeAsync(GameId gameId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 顺序：先删属于这一桌的子表行，再删目录行（"这一桌存在吗"的唯一依据），最后清孤儿。
        // 全程一个事务：删到一半（目录行没了、事件还在）会得到一张"看得见却打不开"的桌。
        var events = await db.Events.Where(row => row.GameId == gameId.Value).ExecuteDeleteAsync(cancellationToken);
        var snapshots = await db.Snapshots.Where(row => row.GameId == gameId.Value).ExecuteDeleteAsync(cancellationToken);
        var receipts = await db.Receipts.Where(row => row.GameId == gameId.Value).ExecuteDeleteAsync(cancellationToken);
        var bindings = await db.SeatBindings.Where(row => row.GameId == gameId.Value).ExecuteDeleteAsync(cancellationToken);
        var games = await db.Games.Where(row => row.GameId == gameId.Value).ExecuteDeleteAsync(cancellationToken);

        // 孤儿：属于**不存在的桌**的行。任何时刻它们都是残渣（目录行没了的桌打不开，
        // 它的历史也没人读得到），而"哪些桌存在"只有这里知道，所以清理放在这条路径上。
        var orphanEvents = await db.Events
            .Where(row => !db.Games.Any(game => game.GameId == row.GameId))
            .ExecuteDeleteAsync(cancellationToken);
        var orphanSnapshots = await db.Snapshots
            .Where(row => !db.Games.Any(game => game.GameId == row.GameId))
            .ExecuteDeleteAsync(cancellationToken);
        var orphanReceipts = await db.Receipts
            .Where(row => !db.Games.Any(game => game.GameId == row.GameId))
            .ExecuteDeleteAsync(cancellationToken);
        var orphanBindings = await db.SeatBindings
            .Where(row => !db.Games.Any(game => game.GameId == row.GameId))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TablePurgeResult(
            events,
            snapshots,
            receipts,
            bindings,
            games,
            orphanEvents + orphanSnapshots + orphanReceipts + orphanBindings);
    }

    /// <summary>把库里的 TEXT 时刻读回成时刻；空值（没有事件 / 没有绑定）原样返回 null。</summary>
    private static DateTimeOffset? Parse(string? stored) =>
        string.IsNullOrEmpty(stored)
            ? null
            : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>事件聚合的投影类型（<see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQueryRaw{TResult}"/> 按属性名对列）。</summary>
    private sealed class EventAggregate
    {
        /// <summary>桌标识。</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>事件条数。</summary>
        public int EventCount { get; set; }

        /// <summary>最后一条事件的记录时刻（库里的 TEXT 形态）。</summary>
        public string? LastRecordedAt { get; set; }

        /// <summary>事件载荷总字节数。</summary>
        public long PayloadBytes { get; set; }
    }

    /// <summary>绑定聚合的投影类型。</summary>
    private sealed class BindingAggregate
    {
        /// <summary>桌标识。</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>最后一次席位认领的时刻（库里的 TEXT 形态）。</summary>
        public string? LastBoundAt { get; set; }
    }
}
