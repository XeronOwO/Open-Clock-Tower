using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OpenClockTower.Server;

/// <summary>
/// 从 **EF 模型**算出"库应该长什么样"（M5 / G-A6-1）：本版的期望结构只有这一个来源。
/// </summary>
/// <remarks>
/// <para>
/// 手写一份期望结构（列一张表、索引一张表）会在改实体时过期，而且过期是静默的——
/// 这正是审计里"索引与约束无人验证"的成因。模型是代码真正要查的东西，
/// 拿它当期望值，比对才有意义：对不上就一定有一个是错的，而且当场就知道是哪一个。
/// </para>
/// <para>
/// 建库 / 补列 / 建索引的**动作**不在这里——那是 <see cref="SchemaMigrationCatalog"/> 的活；
/// 本类只回答"应该是什么样"，不回答"怎么变成那样"。两件事分开，才能一边是声明、一边是执行。
/// </para>
/// </remarks>
public static class SchemaContract
{
    /// <summary>把 <see cref="GameDbContext"/> 的模型渲染成结构快照。</summary>
    public static DatabaseSchema FromModel(GameDbContext db)
    {
        var tables = new List<DatabaseSchema.Table>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            var key = entity.FindPrimaryKey();
            var primaryKey = key is null ? [] : key.Properties.Select(property => property.GetColumnName()).ToList();
            var columns = entity.GetProperties()
                .Select(property => new DatabaseSchema.Column(
                    property.GetColumnName(),
                    property.GetRelationalTypeMapping().StoreType,
                    !property.IsNullable,
                    property.GetDefaultValue() is not null || property.GetDefaultValueSql() is not null,
                    // 位次 1 起，与 SQLite 的 table_info.pk 同口径（0 = 不是主键）。
                    IndexIn(primaryKey, property.GetColumnName()) + 1))
                .ToList();
            var indexes = entity.GetIndexes()
                .Select(index => new DatabaseSchema.Index(
                    index.GetDatabaseName() ?? DefaultIndexName(table, index),
                    [.. index.Properties.Select(property => property.GetColumnName())],
                    index.IsUnique))
                .ToList();

            tables.Add(new DatabaseSchema.Table(table, columns, indexes, primaryKey));
        }

        return new DatabaseSchema([.. tables.OrderBy(table => table.Name, StringComparer.Ordinal)]);
    }

    /// <summary>列在主键里的位次；不在主键里给 -1（调用方 +1 之后正好是 0）。</summary>
    private static int IndexIn(IReadOnlyList<string> primaryKey, string column)
    {
        for (var index = 0; index < primaryKey.Count; index++)
        {
            if (string.Equals(primaryKey[index], column, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// EF 的索引命名约定：<c>IX_表_列1_列2</c>。
    /// </summary>
    /// <remarks>
    /// 兜底用：正常路径下 <c>GetDatabaseName()</c> 在模型定型时已经由约定填好了。
    /// 留着它是因为索引名一旦算错，比对会报出一堆"缺索引 + 多索引"的假差异——那比没有兜底更误导人。
    /// </remarks>
    private static string DefaultIndexName(string table, IReadOnlyIndex index) =>
        $"IX_{table}_{string.Join("_", index.Properties.Select(property => property.GetColumnName()))}";
}
