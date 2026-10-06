using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 启动引导：建库 → 结构守卫（账号表 / 后加的列）→ 装载**所有**桌。
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
            await EnsureGameColumnsAsync(db, cancellationToken);
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
    /// 会话表的**加列守卫**：大厅元数据给 <c>Games</c> 增了列，而 <c>EnsureCreated</c> 只建不改。
    /// 缺列时补上（SQLite 的 <c>ADD COLUMN</c> 是原地操作、不动既有数据），
    /// 于是**升级不会让你打不开原来那一桌**。
    /// </summary>
    /// <remarks>
    /// 这是本版唯一的 DDL 例外，且只做"加列"：删列 / 改类型 / 加约束仍主张换新库。
    /// 换掉 SQLite provider 时，这里要一并换成正式迁移（D-0004 允许换 provider）。
    /// </remarks>
    private async Task EnsureGameColumnsAsync(GameDbContext db, CancellationToken cancellationToken)
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
    }
}
