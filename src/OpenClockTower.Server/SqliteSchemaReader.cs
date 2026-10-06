using System.Data;
using System.Data.Common;

namespace OpenClockTower.Server;

/// <summary>
/// 从库里**读出现有结构**（M5 / G-A6-1）：表、列（类型与 NOT NULL）、主键位、显式索引。
/// </summary>
/// <remarks>
/// <para>
/// 全部走 SQLite 自己的 <c>PRAGMA</c>（<c>table_info</c> / <c>index_list</c> / <c>index_info</c>），
/// 不查 EF 的模型——本类要回答的正是"**磁盘上这个库**长什么样"，
/// 拿模型来回答等于自己问自己（那正是旧守卫的毛病：它只比列名，索引与约束连问都没问）。
/// </para>
/// <para>
/// 只读：一个 <c>PRAGMA</c> 查询都不改库（写口径的那两条在 <see cref="SqliteConnectionPragmas"/> 里，不在这里）。
/// </para>
/// </remarks>
public static class SqliteSchemaReader
{
    /// <summary>读出一个库的完整结构快照。</summary>
    /// <param name="connection">库连接；关着的会被临时打开，用完按原状态归还。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<DatabaseSchema> ReadAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var tables = new List<DatabaseSchema.Table>();
            foreach (var table in await ReadTableNamesAsync(connection, cancellationToken))
            {
                tables.Add(new DatabaseSchema.Table(
                    table,
                    await ReadColumnsAsync(connection, table, cancellationToken),
                    await ReadIndexesAsync(connection, table, cancellationToken),
                    await ReadPrimaryKeyAsync(connection, table, cancellationToken)));
            }

            return new DatabaseSchema(tables);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>用户表（<c>sqlite_%</c> 那几张是 SQLite 自己的：<c>sqlite_sequence</c> 之类不算结构）。</summary>
    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>逐列读：列名 / 声明类型 / NOT NULL / 主键位次。</summary>
    private static async Task<IReadOnlyList<DatabaseSchema.Column>> ReadColumnsAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var columns = new List<DatabaseSchema.Column>();
        await using var command = connection.CreateCommand();
        // 表名来自本库自己的 sqlite_master（见上一步），不是外部输入；仍然加引号。
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // 列 1 = 列名，2 = 声明类型，3 = NOT NULL，4 = 默认值，5 = 主键位次（0 = 不是主键）。
            columns.Add(new DatabaseSchema.Column(
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetInt32(3) != 0,
                !reader.IsDBNull(4),
                reader.GetInt32(5)));
        }

        return columns;
    }

    /// <summary>主键列，按主键里的次序（<c>table_info</c> 的位次不是声明顺序，要靠它排回来）。</summary>
    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var columns = await ReadColumnsAsync(connection, table, cancellationToken);
        return [.. columns.Where(column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(column => column.PrimaryKeyOrdinal)
            .Select(column => column.Name)];
    }

    /// <summary>
    /// 显式建出来的索引：<c>origin='c'</c> 才算。
    /// </summary>
    /// <remarks>
    /// <c>origin</c> 的三个取值分别是：<c>c</c> = <c>CREATE INDEX</c> 建的、<c>u</c> = 唯一约束自带的、
    /// <c>pk</c> = 主键自带的。后两类是 SQLite 为约束自动造的 <c>sqlite_autoindex_*</c>，
    /// 它们随建表语句走、没有独立的名字，混进来只会让比对出现两边都数不清的差异。
    /// </remarks>
    private static async Task<IReadOnlyList<DatabaseSchema.Index>> ReadIndexesAsync(
        DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var indexes = new List<DatabaseSchema.Index>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list(\"{table}\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // 列 1 = 索引名，2 = 是否唯一，3 = 来源（'c' / 'u' / 'pk'）。
                if (reader.GetString(3) != "c")
                {
                    continue;
                }

                indexes.Add(new DatabaseSchema.Index(
                    reader.GetString(1),
                    await ReadIndexColumnsAsync(connection, reader.GetString(1), cancellationToken),
                    reader.GetInt32(2) != 0));
            }
        }

        return indexes;
    }

    /// <summary>一个索引覆盖哪些列，按索引里的次序。</summary>
    private static async Task<IReadOnlyList<string>> ReadIndexColumnsAsync(
        DbConnection connection,
        string index,
        CancellationToken cancellationToken)
    {
        var columns = new List<(int Ordinal, string Name)>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info(\"{index}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // 列 0 = 索引里的位次，2 = 列名（表达式索引这里是 NULL，本项目不用表达式索引）。
            if (!reader.IsDBNull(2))
            {
                columns.Add((reader.GetInt32(0), reader.GetString(2)));
            }
        }

        return [.. columns.OrderBy(column => column.Ordinal).Select(column => column.Name)];
    }
}
