using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 启动引导：建库 → 结构守卫（账号表 / 会话表的列对账）→ 装载**所有**桌。
/// </summary>
/// <remarks>
/// <para>
/// 多桌（D-0024）之后，"恢复"不再是对一个进程级会话调用一次，而是由 <see cref="GameRegistry"/>
/// 把库里每一桌都装起来；本服务负责建库与结构守卫。
/// </para>
/// <para>
/// **不再创建任何桌**（D-0027）：默认桌先天没有开桌账号，与"说书人即房主"不相容。
/// 全新部署启动后库里是空的，第一桌由人在界面上开出来——"打开站点是空大厅"是有意的初始状态。
/// </para>
/// <para>
/// 夜晚计划**不在引导阶段自动构建**：真实顺序表在 <c>OpenClockTower.Rules</c>，
/// 按它建表需要角色分配与角色行动契约。在那之前，开阶段是宿主 / 说书人的显式动作。
/// </para>
/// <para>
/// 单桌装载失败**不阻断启动**：失败的那一桌自带降级位（room health），说书人可在界面里显式重建，
/// 一格坏桌不该让别的桌开不了（多桌的可用性要求）。
/// </para>
/// </remarks>
public sealed class GameBootstrapHostedService : IHostedService
{
    private readonly IDbContextFactory<GameDbContext> _dbFactory;
    private readonly GameRegistry _registry;
    private readonly ILogger<GameBootstrapHostedService> _logger;

    /// <summary>说书人票据时代的列（D-0027 起不再映射；老库里那一列由结构守卫清掉）。</summary>
    private const string RetiredTicketColumn = "StorytellerTicket";

    /// <summary>清掉退场列：<c>NOT NULL</c> 且无默认值的列会让新行的 INSERT 直接被拒。</summary>
    private const string DropRetiredTicketColumnSql = "ALTER TABLE Games DROP COLUMN StorytellerTicket;";

    /// <summary>构造引导服务。</summary>
    public GameBootstrapHostedService(
        IDbContextFactory<GameDbContext> dbFactory,
        GameRegistry registry,
        ILogger<GameBootstrapHostedService> logger)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureAccountSchemaAsync(db, cancellationToken);
            await EnsureGameSchemaAsync(db, cancellationToken);
        }

        // 装载库里全部在册的桌（多桌并行）。库是空的就什么都不装——等第一桌被开出来。
        await _registry.InitializeAsync(cancellationToken);

        _logger.LogInformation(
            "在册的桌：数量={Count} 标识={Ids}",
            _registry.GameIds.Count,
            string.Join(",", _registry.GameIds.Select(id => id.Value)));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 账号表守卫（D-0021）：现库用 <c>EnsureCreated</c>，不会给已存在的库补表；
    /// 缺表时**显式失败**并提示换新库——不做在线迁移、不静默继续。
    /// </summary>
    private async Task EnsureAccountSchemaAsync(GameDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            _ = await db.Users.AsNoTracking().AnyAsync(cancellationToken);
            _ = await db.SeatBindings.AsNoTracking().AnyAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogCritical(
                exception,
                "旧库缺少账号 / 席位绑定表：本版不做在线迁移，请换新库（D-0021）");
            throw;
        }
    }

    /// <summary>
    /// 会话表的结构守卫：**缺列补上、退场列清掉**——<c>EnsureCreated</c> 只建不改，
    /// 老库不会自己长成新形态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两种动作都是原地操作（SQLite 的 <c>ADD COLUMN</c> / <c>DROP COLUMN</c>），不碰别列的数据，
    /// 于是**升级既不会让你打不开原来那一桌，也不会让你开不了新桌**。
    /// </para>
    /// <para>
    /// 这是本版唯一的 DDL 例外，且只做这两类：改类型 / 加约束 / 重建表仍主张换新库。
    /// 换掉 SQLite provider 时，这里要一并换成正式迁移（D-0004 允许换 provider）。
    /// </para>
    /// </remarks>
    private async Task EnsureGameSchemaAsync(GameDbContext db, CancellationToken cancellationToken)
    {
        var existing = await ReadGameColumnsAsync(db, cancellationToken);

        // 每项：列名 + 追加语句（NOT NULL 在 SQLite 上必须带默认值才被接受）。
        (string Column, string Sql)[] required =
        [
            ("Name", "ALTER TABLE Games ADD COLUMN Name TEXT NOT NULL DEFAULT '';"),
            ("IsLocked", "ALTER TABLE Games ADD COLUMN IsLocked INTEGER NOT NULL DEFAULT 0;"),
            // 归属（D-0027）：老库里的桌补成 NULL = 没有房主，谁都进不去它的主持台——
            // 这是如实反映"升级前那一桌本来就没有开桌账号"，不做任何猜测性回填。
            ("CreatedByAccountId", "ALTER TABLE Games ADD COLUMN CreatedByAccountId INTEGER NULL;"),
        ];

        foreach (var (column, sql) in required)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                _logger.LogWarning("旧库补列（升级兼容）：Games.{Column}", column);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogCritical(
                    exception,
                    "补列失败：Games.{Column}——库结构与本版不匹配，请换新库",
                    column);
                throw;
            }
        }

        // 退场列（D-0027）：说书人票据时代的凭据，本版不再映射它。留着不只是"没用"——
        // 老库那一列是 **NOT NULL 且没有默认值**，于是**开新桌的 INSERT 会被它当场拒掉**
        // （实测：NOT NULL constraint failed: Games.StorytellerTicket），
        // 表现成最难查的那种半截升级："原来那一桌读得出，新桌开不了"。
        // 删掉它，老库与新库同形态；顺带把退场的凭据从磁盘上抹掉。
        if (existing.Contains(RetiredTicketColumn))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(DropRetiredTicketColumnSql, cancellationToken);
                _logger.LogWarning("旧库删列（退场凭据）：Games.{Column}", RetiredTicketColumn);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogCritical(
                    exception,
                    "删列失败：Games.{Column}——库结构与本版不匹配，请换新库",
                    RetiredTicketColumn);
                throw;
            }
        }
    }

    /// <summary>读出 <c>Games</c> 现有的列名（对账的依据）。</summary>
    /// <remarks>连的是上下文自己的连接，用完按原状态归还——开着的不要替调用方关掉。</remarks>
    private static async Task<HashSet<string>> ReadGameColumnsAsync(GameDbContext db, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(Games);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return existing;
    }
}
