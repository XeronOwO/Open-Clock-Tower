using System.Data.Common;
using System.Globalization;

namespace OpenClockTower.Server;

/// <summary>
/// SQLite 运行口径的**唯一出口**（M5 / G-A6-8）：日志模式、同步级别、写锁等待都只在这里声明。
/// </summary>
/// <remarks>
/// <para>
/// 审计发现的两件事都出在这里：①全仓库没有一个 <c>PRAGMA</c> 是写口径的——部署实例上的
/// <c>journal_mode = wal</c> 是**人工设的**，全新部署跑起来是默认的 <c>delete</c> 模式；
/// ②审计读数里的 <c>busy_timeout = 0</c> **其实是 <c>sqlite3</c> 命令行自己那条连接的读数**，
/// 不是应用的——但应用侧同样没有任何显式口径，实际等多久取决于框架默认值，没人知道。
/// </para>
/// <para>
/// 三条口径各自的理由：
/// </para>
/// <list type="number">
/// <item>
/// <b><c>journal_mode = wal</c></b>（**持久属性**，写在库文件头里，设一次即可）：
/// 读不挡写、写不挡读，玩家读复盘不会把正在写入的事件卡住；崩溃后由 WAL 自动前滚。
/// </item>
/// <item>
/// <b><c>synchronous = FULL</c></b>（连接级）：WAL 下 <c>NORMAL</c> 更快，但**掉电可能丢掉最后几笔已提交的事务**——
/// 而本项目的事件流就是"这一局发生过什么"的唯一记录，丢一笔就等于复盘与状态账对不上。
/// 这是有意用吞吐换正确性：一局游戏每秒也就几笔写入，<c>fsync</c> 的代价看不见。
/// </item>
/// <item>
/// <b><c>busy_timeout</c></b>（连接级，取值来自 <see cref="SqliteOptions.BusyTimeoutMilliseconds"/>）：
/// 撞上写锁时先排队等待而不是立刻抛 <c>SQLITE_BUSY</c>。
/// </item>
/// </list>
/// </remarks>
public static class SqliteConnectionPragmas
{
    /// <summary>日志模式：WAL（持久属性）。</summary>
    public const string JournalMode = "wal";

    /// <summary>同步级别：FULL（连接级）。</summary>
    public const string Synchronous = "FULL";

    /// <summary>
    /// 给**一个连接**设上"每个连接都要成立"的那两条口径（同步级别 + 写锁等待）。
    /// </summary>
    /// <remarks>
    /// 刻意不用连接串关键字：SQLite 的连接串里没有 <c>busy_timeout</c> 这一项，
    /// 靠框架默认值等于把口径交给别人的版本号。这两条 PRAGMA 不碰磁盘（只设进程内参数），
    /// 所以每个连接上线时都设一遍没有性能顾虑。
    /// </remarks>
    public static void ApplyPerConnection(DbConnection connection, SqliteOptions options)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PerConnectionStatements(options);
        command.ExecuteNonQuery();
    }

    /// <summary><see cref="ApplyPerConnection"/> 的异步版本（EF 的异步打开路径走它）。</summary>
    public static async Task ApplyPerConnectionAsync(
        DbConnection connection,
        SqliteOptions options,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PerConnectionStatements(options);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 设日志模式并**读回实际生效的值**（持久属性，只在建库时设一次）。
    /// </summary>
    /// <remarks>
    /// 读回是必须的：<c>PRAGMA journal_mode=wal</c> 在只读库、内存库或已经有别的连接持有排他锁时
    /// **会静默失败并保持原模式**。启动日志里那行读数因此是"真的生效了没有"的判据，而不是"我设过了"。
    /// </remarks>
    public static async Task<string> ApplyJournalModeAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode={JournalMode};";
        var effective = await command.ExecuteScalarAsync(cancellationToken);
        return effective as string ?? string.Empty;
    }

    /// <summary>读出一个连接当前的忙等上限（毫秒）。</summary>
    public static async Task<long> ReadBusyTimeoutAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? -1 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>连接级口径的语句文本（同步级别 + 忙等；取值经过钳制，不拼外部字符串）。</summary>
    private static string PerConnectionStatements(SqliteOptions options)
    {
        var busyTimeout = Math.Clamp(options.BusyTimeoutMilliseconds, 0, 600_000);
        return $"PRAGMA busy_timeout={busyTimeout.ToString(CultureInfo.InvariantCulture)};"
               + $"PRAGMA synchronous={Synchronous};";
    }
}
