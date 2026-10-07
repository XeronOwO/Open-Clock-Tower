using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OpenClockTower.Server;

/// <summary>
/// 库的**只读体检**（M5 / G-A6-4）：能打开吗、有没有坏、里面有什么。
/// </summary>
/// <remarks>
/// <para>
/// 一律以 <c>Mode=ReadOnly</c> 打开：体检的对象可能是**正在服务的库**，也可能是一份**备份**——
/// 两种情况下都不允许"看一下"顺手改掉什么（建表 / 补列那种动作归
/// <see cref="SchemaMigrationCatalog"/>，只在启动时跑，这里禁止）。
/// </para>
/// <para>
/// 体检是运维的第一手读数：备份命令拿它判"这份备份能不能用"，恢复演练拿它判"恢复出来的库是不是那一份"。
/// </para>
/// </remarks>
public static class DatabaseInspector
{
    /// <summary>体检一个库文件（只读，不建表、不写任何东西）。</summary>
    /// <param name="databasePath">库文件路径。</param>
    /// <param name="options">连接口径（忙等取值）。</param>
    public static DatabaseReport Inspect(string databasePath, SqliteOptions options)
    {
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"数据库不存在：{path}");
        }

        using var connection = OpenReadOnly(path);
        SqliteConnectionPragmas.ApplyPerConnection(connection, options);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var pageSize = ReadInt(connection, "PRAGMA page_size;");
        var pageCount = ReadInt(connection, "PRAGMA page_count;");
        var freelistCount = ReadInt(connection, "PRAGMA freelist_count;");
        var journalMode = ReadText(connection, "PRAGMA journal_mode;");
        // 结构版本（M5 / G-A6-2）：只读一个头字段。体检是"动手之前先看一眼"的动作，
        // 而"这个库比程序新还是旧"正是动手之前最该知道的一件事（回滚、恢复都用得上）。
        var schemaVersion = ReadInt(connection, "PRAGMA user_version;");
        // 完整性检查刻意留到最后：它是唯一会**逐页读**的动作，坏库上它会以异常的形式报出来。
        var integrity = ReadText(connection, "PRAGMA integrity_check;");

        var counts = new DatabaseReport.TableCounts(
            CountOf(connection, tables, "Games"),
            CountOf(connection, tables, "Events"),
            CountOf(connection, tables, "Users"),
            CountOf(connection, tables, "Snapshots"),
            CountOf(connection, tables, "Receipts"),
            CountOf(connection, tables, "SeatBindings"));

        var games = ReadGames(connection, tables);

        return new DatabaseReport(
            path,
            new FileInfo(path).Length,
            Sha256Of(path),
            integrity,
            journalMode,
            schemaVersion,
            SchemaMigrationCatalog.LatestVersion,
            pageSize,
            pageCount,
            freelistCount,
            counts,
            games,
            // 体积（M5 / G-A6-5）：每一局的载荷字节加起来就是总量，不必再查一遍。
            games.Sum(game => game.PayloadBytes));
    }

    /// <summary>
    /// 以"只读"的方式打开一个库文件（体检的第一原则：不许改它）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 正常路径是 SQLite 自己的只读模式（<c>Mode=ReadOnly</c>）：物理上就打不开写入。
    /// </para>
    /// <para>
    /// 有一种库打不开它：**自报 WAL、但伴生文件已经不在的库**——服务停干净之后 <c>-wal</c> / <c>-shm</c>
    /// 会被清掉，而只读连接建不出 <c>-shm</c>，于是 SQLite 报 <c>unable to open database file</c>。
    /// 这时退一步用读写方式打开，再用 <c>query_only</c> 把这条连接变成"写就报错"：
    /// 副作用只是 SQLite 自己补一个 <c>-shm</c>，库内容一个字节都不动。
    /// </para>
    /// <para>
    /// 与备份同一条理由不挂连接池（<c>Pooling=False</c>）：体检完就要能把文件交给别人（复制、删除、比对）。
    /// </para>
    /// </remarks>
    private static SqliteConnection OpenReadOnly(string path)
    {
        var readOnly = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        try
        {
            readOnly.Open();
            return readOnly;
        }
        catch (SqliteException)
        {
            readOnly.Dispose();
        }

        var writable = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        writable.Open();
        using var command = writable.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON;";
        command.ExecuteNonQuery();
        return writable;
    }

    /// <summary>
    /// 逐局读数：桌名 / 席位数 / 事件条数 / 最后序号 / 载荷字节——"这一局的复盘还在不在、有多大"就是这几列。
    /// </summary>
    /// <remarks>
    /// 事件那几列走**一条 <c>GROUP BY</c>**（不是逐桌查一遍）：体检会在服务跑着的时候做，
    /// 桌数一多，逐桌查就是拿运维动作去压生产库（审计 G-A5-5 的 N+1 是同一个病）。
    /// 载荷按**字节**量（<c>CAST AS BLOB</c>）：中文载荷一个字三字节，按字符数量会低估三倍。
    /// </remarks>
    private static IReadOnlyList<DatabaseReport.GameLine> ReadGames(SqliteConnection connection, HashSet<string> tables)
    {
        if (!tables.Contains("Games"))
        {
            return [];
        }

        var metrics = new Dictionary<string, (int Events, long Last, long Bytes)>(StringComparer.Ordinal);
        if (tables.Contains("Events"))
        {
            try
            {
                using var aggregate = connection.CreateCommand();
                aggregate.CommandText =
                    "SELECT GameId, COUNT(*), COALESCE(MAX(Sequence), 0), COALESCE(SUM(LENGTH(CAST(Payload AS BLOB))), 0) "
                    + "FROM Events GROUP BY GameId;";
                using var reader = aggregate.ExecuteReader();
                while (reader.Read())
                {
                    metrics[reader.GetString(0)] = (reader.GetInt32(1), reader.GetInt64(2), reader.GetInt64(3));
                }
            }
            catch (SqliteException)
            {
                // **坏库也要读得出读数**：体检的价值恰恰在"这份备份还能不能用"，
                // 那一刻它多半已经坏了。事件那几列读不出来就按 0 报，别的读数照给——
                // 真相由「体检：完整性=…」那一行承担，不靠这里抛异常（实测：拿一个被灌了垃圾的
                // 备份来体检，逐页扫描的 integrity_check 能报出问题，而这张表的聚合查询会直接失败）。
            }
        }

        var lines = new List<DatabaseReport.GameLine>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT GameId, Name, SeatsJson FROM Games ORDER BY GameId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var gameId = reader.GetString(0);
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var seatCount = reader.IsDBNull(2) ? 0 : CountSeats(reader.GetString(2));
                var metric = metrics.GetValueOrDefault(gameId);
                lines.Add(new DatabaseReport.GameLine(
                    gameId,
                    name,
                    seatCount,
                    metric.Events,
                    metric.Last,
                    metric.Bytes));
            }
        }

        return lines;
    }

    /// <summary>数出席位数组的长度（库里的席位是 JSON 数组，不是一张表）。</summary>
    private static int CountSeats(string seatsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(seatsJson);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            // 库里的席位串坏了不该让"体检"整个失败：这一列读不出来就报 0，别的读数照给。
            return 0;
        }
    }

    private static int CountOf(SqliteConnection connection, HashSet<string> tables, string table)
    {
        if (!tables.Contains(table))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        // 表名来自本库自己的 sqlite_master，不是外部输入；仍然加引号，免得将来有表名带连字符。
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static int ReadInt(SqliteConnection connection, string sql) =>
        Convert.ToInt32(ReadScalar(connection, sql) ?? 0, CultureInfo.InvariantCulture);

    private static string ReadText(SqliteConnection connection, string sql) =>
        Convert.ToString(ReadScalar(connection, sql), CultureInfo.InvariantCulture) ?? string.Empty;

    private static object? ReadScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>整文件的 SHA-256：异地备份"传过去的是不是同一份"就靠它。</summary>
    /// <remarks>
    /// 共享模式必须放宽到读写：体检的对象常常是**正在服务的库**，Windows 上以"只许别人读"的方式
    /// 打开一个已经有写句柄的文件会直接吃 sharing violation（实测踩到）。
    /// </remarks>
    private static string Sha256Of(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
