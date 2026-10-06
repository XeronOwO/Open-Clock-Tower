using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OpenClockTower.Server;

/// <summary>
/// 启动时的**结构升级**（M5 / G-A6-2）：读版本 → 拦"库比程序新" → 逐条应用欠下的迁移 → 核对结构。
/// </summary>
/// <remarks>
/// <para>
/// 一条迁移 = 一个事务：DDL 与 <c>user_version</c> 一起提交，所以不存在"结构改了、版本没记上"
/// （那会让下一次启动把同一条迁移再跑一遍，或者反过来漏跑）。SQLite 的 DDL 是事务性的，这一点成立。
/// </para>
/// <para>
/// <b>版本只进不退</b>：库的版本比程序支持的最新版还大，说明这个库被更新版的程序改过。
/// 本版不会"尽量试试"——降级后的程序看不懂新结构，继续跑只会把数据写坏。
/// 这时唯一的出路是换回那个版本，或者用升级前的备份恢复（部署文档 §9.3）。
/// </para>
/// <para>
/// 与迁移**分开**的两件事：迁移负责"变成该有的样子"，<see cref="SchemaGuard"/> 负责"核对真的是那个样子"。
/// 合在一起写会变成"我执行了所以我对了"，而审计要的判据恰恰是"删掉一条唯一索引后，守卫能报出来并自愈"。
/// </para>
/// </remarks>
public static class DatabaseSchemaUpgrader
{
    /// <summary>把库升到本版支持的结构版本；返回升完之后的版本号。</summary>
    public static async Task<int> UpgradeAsync(GameDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            var applied = await SqliteUserVersion.ReadAsync(connection, cancellationToken);
            var latest = SchemaMigrationCatalog.LatestVersion;
            if (applied > latest)
            {
                logger.LogCritical(
                    "库的结构版本比本程序支持的更新：库={Applied} · 本程序支持到={Latest}。"
                    + "迁移只进不退，本版拒绝在这个库上启动（继续跑会写坏新结构）。",
                    applied,
                    latest);
                throw new InvalidOperationException(
                    $"库的结构版本（{applied}）比本程序支持的（{latest}）新：这个库被更新版的程序改过。"
                    + "请换回那个版本，或用升级前的备份恢复（部署文档 §9.3）。");
            }

            foreach (var migration in SchemaMigrationCatalog.Pending(applied))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await migration.Apply(new SchemaMigrationContext(db), cancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    SqliteUserVersion.Assignment(migration.Version),
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                applied = migration.Version;

                logger.LogInformation(
                    "应用迁移 v{Version}：{Description}",
                    migration.Version,
                    migration.Description);
                if (migration.IsIrreversible)
                {
                    // 不可逆的要单独喊一声：它决定"这个库还能不能配旧程序回滚"。
                    logger.LogWarning(
                        "迁移 v{Version} 不可逆：回滚程序时必须连库一起回（部署文档 §9.3）",
                        migration.Version);
                }
            }

            if (applied == 0)
            {
                // 迁移清单为空才会走到这里（清单至少有一条 v1）；写成显式失败而不是静默继续。
                throw new InvalidOperationException("迁移清单是空的：没有 v1 就建不出库（SchemaMigrationCatalog）。");
            }

            await SchemaGuard.EnforceAsync(db, logger, cancellationToken);
            return applied;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
