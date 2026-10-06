using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 启动引导：**结构迁移与核对** → SQLite 运行口径读数 → 装载**所有**桌。
/// </summary>
/// <remarks>
/// <para>
/// 多桌（D-0024）之后，"恢复"不再是对一个进程级会话调用一次，而是由 <see cref="GameRegistry"/>
/// 把库里每一桌都装起来；本服务负责让库先变成可用形态。
/// </para>
/// <para>
/// **结构那一半已经搬走**（M5 / G-A6-2）：建表 / 补列 / 建索引的语句全部归
/// <see cref="SchemaMigrationCatalog"/>，本服务只按顺序调用
/// <see cref="DatabaseSchemaUpgrader"/>（读版本 → 跑欠下的迁移 → 核对结构）与口径读数。
/// 从前那种"每加一列就往守卫里补一行"的写法没有了——那不是机制，是记账。
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
/// 单桌装载失败**不阻断启动**：失败的那一桌自带降位（room health），说书人可在界面里显式重建，
/// 一格坏桌不该让别的桌开不了（多桌的可用性要求）。
/// </para>
/// </remarks>
public sealed class GameBootstrapHostedService : IHostedService
{
    private readonly IDbContextFactory<GameDbContext> _dbFactory;
    private readonly GameRegistry _registry;
    private readonly SqliteOptions _sqliteOptions;
    private readonly ILogger<GameBootstrapHostedService> _logger;

    /// <summary>构造引导服务。</summary>
    public GameBootstrapHostedService(
        IDbContextFactory<GameDbContext> dbFactory,
        GameRegistry registry,
        SqliteOptions sqliteOptions,
        ILogger<GameBootstrapHostedService> logger)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _sqliteOptions = sqliteOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var schemaVersion = await DatabaseSchemaUpgrader.UpgradeAsync(db, _logger, cancellationToken);
            await ApplySqliteRuntimeAsync(db, schemaVersion, cancellationToken);
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
    /// SQLite 运行口径（M5 / G-A6-8）：**日志模式设一次**（持久属性写在库文件里），
    /// 同步级别与忙等由拦截器在每条连接上设——这里读回实际生效的值并记一行启动读数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 读回而不是"设完就算"：<c>PRAGMA journal_mode=wal</c> 在只读文件系统、或者库被别的连接
    /// 独占时会**静默保持原模式**。已有库的日志模式取决于它的来历（从备份恢复回来的库是
    /// 单文件 rollback 形态），所以这一行日志是"这台机器上到底哪种模式"的唯一判据。
    /// </para>
    /// <para>
    /// 连接从 EF 的打开路径拿（<c>OpenConnectionAsync</c>）：这样读到的忙等就是**拦截器真的生效了**的证据，
    /// 而不是这条代码自己临时设的值。
    /// </para>
    /// </remarks>
    private async Task ApplySqliteRuntimeAsync(
        GameDbContext db,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var journalMode = await SqliteConnectionPragmas.ApplyJournalModeAsync(connection, cancellationToken);
            var busyTimeout = await SqliteConnectionPragmas.ReadBusyTimeoutAsync(connection, cancellationToken);
            var path = connection.DataSource;
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;

            _logger.LogInformation(
                "数据库口径：库={Path} · 大小={Size}B · 结构版本={SchemaVersion}/{LatestVersion} · "
                + "日志模式={JournalMode} · 同步级别={Synchronous} · 写锁等待={BusyTimeout}ms",
                path,
                size,
                schemaVersion,
                SchemaMigrationCatalog.LatestVersion,
                journalMode,
                SqliteConnectionPragmas.Synchronous,
                busyTimeout);

            if (!string.Equals(journalMode, SqliteConnectionPragmas.JournalMode, StringComparison.OrdinalIgnoreCase))
            {
                // 不打断启动（服务照常能跑），但必须看得见：这条口径决定崩溃恢复与并发读写的形态。
                _logger.LogWarning(
                    "日志模式没有生效：期望 {Expected}，实际 {Actual}——只读文件系统、库被独占，或这个库不是本进程建的。",
                    SqliteConnectionPragmas.JournalMode,
                    journalMode);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
