using Microsoft.EntityFrameworkCore;

namespace OpenClockTower.Server;

/// <summary>
/// 一条迁移能用的**全部原语**（M5 / G-A6-2）：看一眼现状，再动一下库。
/// </summary>
/// <remarks>
/// <para>
/// 刻意只有两个动作：<see cref="ReadSchemaAsync"/>（只读，整个结构）与 <see cref="ExecuteAsync"/>（执行一条 SQL）。
/// 迁移拿不到 <see cref="GameDbContext"/> 本身，也就没法顺手改实体、跑查询、提交事务——
/// 事务与版本号的记账由 <see cref="DatabaseSchemaUpgrader"/> 统一负责，迁移只管"这一步做什么"。
/// </para>
/// <para>
/// <see cref="ReadSchemaAsync"/> 给的是**整个结构**而不是"某一列在不在"，因为迁移里真正要写的判断
/// 通常是"表在不在 / 这一列在不在 / 这个索引建过没有"，而它们都从同一份快照上问，读一次就够。
/// </para>
/// </remarks>
public sealed class SchemaMigrationContext
{
    private readonly GameDbContext _db;

    /// <summary>构造上下文（只由升级器构造：迁移不该自己找库）。</summary>
    internal SchemaMigrationContext(GameDbContext db) => _db = db;

    /// <summary>读一眼库里现在长什么样（只读）。</summary>
    public Task<DatabaseSchema> ReadSchemaAsync(CancellationToken cancellationToken) =>
        SqliteSchemaReader.ReadAsync(_db.Database.GetDbConnection(), cancellationToken);

    /// <summary>执行一条 SQL（DDL 或数据语句），在升级器开好的那个事务里。</summary>
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
}
