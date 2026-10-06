using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenClockTower.Server;

/// <summary>
/// 库的**热备份**（M5 / G-A6-4）：服务照常跑着也能备，备完当场体检，顺手做轮转。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不是部署文档里那三行 <c>stop / cp / start</c>：停服复制会**真的把玩家踢下线**，
/// 而"备份要停服"这条纪律在没有依据时只会被绕过（审计原文：这份文档里 WAL 一个字都没写）。
/// 在线备份 API 由 SQLite 自己保证一致性（读事务 + 原子写目标文件），
/// 所以备份可以每天都做，也可以在想做的时候随时做。
/// </para>
/// <para>
/// **备份不留活口**：目标文件已存在时绝不覆盖（同一秒内再备一次会追加序号），
/// 完整性检查没过时会**删掉刚写出来的坏备份**并报错——留着一份"看起来有、其实是坏的"备份，
/// 比没有备份更危险。
/// </para>
/// <para>
/// 轮转只作用于**本工具自己产出的文件名形态**（<c>oct-&lt;UTC 时刻&gt;.db</c>），
/// 目录里的其它文件一个都不碰：这个目录同时放着别人手工拷的快照时，工具不该替他做决定。
/// </para>
/// </remarks>
public static class DatabaseBackup
{
    /// <summary>备份文件名前缀。</summary>
    public const string FilePrefix = "oct-";

    /// <summary>备份文件名后缀。</summary>
    public const string FileExtension = ".db";

    /// <summary>保留份数的默认值（够覆盖一周的每日备份）。</summary>
    public const int DefaultKeep = 7;

    /// <summary>文件名里的时刻形态（UTC，按名字排序即按时间排序）。</summary>
    private const string StampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>
    /// 从 <paramref name="sourcePath"/> 热备一份到 <paramref name="outputDirectory"/>，保留最近 <paramref name="keep"/> 份。
    /// </summary>
    /// <returns>**备份文件**的体检读数（不是源库的）。</returns>
    public static DatabaseReport Write(
        string sourcePath,
        string outputDirectory,
        int keep,
        SqliteOptions options,
        TextWriter output)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new InvalidOperationException($"数据库不存在：{source}");
        }

        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var destination = NextDestinationPath(directory);

        try
        {
            output.WriteLine($"热备份：{source}");
            // Pooling=False：维护命令是"跑完就走"的进程，句柄要当场还给文件系统——
            // 连接池握着一份写句柄会让后面的体检、轮转、复制全部撞上"文件被占用"。
            using (var sourceConnection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                sourceConnection.Open();
                using var destinationConnection = new SqliteConnection($"Data Source={destination};Pooling=False");
                destinationConnection.Open();
                // SQLite 的在线备份 API：源库在跑也没关系，一致性由它自己保证。
                sourceConnection.BackupDatabase(destinationConnection);
                NormalizeJournalMode(destinationConnection);
            }

            RestrictToOwner(destination);
            var report = DatabaseInspector.Inspect(destination, options);
            if (!string.Equals(report.Integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                throw new InvalidOperationException(
                    $"备份完整性检查未通过（{report.Integrity}）：已删除这份坏备份 {destination}，源库未被改动");
            }

            output.WriteLine(report.Describe());
            var removed = Rotate(directory, keep);
            output.WriteLine(
                $"轮转：保留最近 {Math.Max(1, keep)} 份，本次删除 {removed.Count} 份"
                + (removed.Count == 0 ? string.Empty : $"（{string.Join("、", removed)}）"));
            return report;
        }
        catch
        {
            // 半截文件不许留下：一次失败的备份必须看起来像"没备过"。
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    /// <summary>
    /// 把备份文件收成**单文件自足**形态：普通回滚日志（<c>delete</c>）、没有 <c>-wal</c> / <c>-shm</c> 伴生文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 备份 API 会把源库头里的日志模式一起复制过来，于是从 WAL 源备出来的文件**自称 WAL**、
    /// 却没有任何 WAL 伴生文件——这种文件连"只读打开"都会被拒（SQLite 建不出 <c>-shm</c>），
    /// 实测报 <c>unable to open database file</c>。备份是拿去异地存、将来直接放回去的东西，
    /// 必须是最普通、任何工具都能读的形态。
    /// </para>
    /// <para>
    /// 改回 WAL 由应用负责：启动引导按口径设一次（G-A6-8），所以恢复出来的库照样是 WAL——
    /// 备份形态与运行形态不必是同一个东西。
    /// </para>
    /// </remarks>
    private static void NormalizeJournalMode(SqliteConnection destinationConnection)
    {
        using var command = destinationConnection.CreateCommand();
        // 这一步会顺带把 WAL 里的内容并回主文件并删掉伴生文件。
        command.CommandText = "PRAGMA journal_mode=delete;";
        command.ExecuteScalar();
    }

    /// <summary>给下一次备份取一个不会撞车的文件名（同一秒内再备一次会追加序号，绝不覆盖）。</summary>
    private static string NextDestinationPath(string directory)
    {
        var stamp = DateTimeOffset.UtcNow.ToString(StampFormat, CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{FilePrefix}{stamp}{FileExtension}");
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{FilePrefix}{stamp}-{index}{FileExtension}");
            index++;
        }

        return candidate;
    }

    /// <summary>按"保留最近 N 份"清理本工具自己产出的备份，返回被删掉的文件名。</summary>
    private static IReadOnlyList<string> Rotate(string directory, int keep)
    {
        var effective = Math.Max(1, keep);
        var candidates = Directory
            .EnumerateFiles(directory, FilePrefix + "*" + FileExtension, SearchOption.TopDirectoryOnly)
            .Where(path => IsBackupName(Path.GetFileName(path)))
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        var removed = new List<string>();
        foreach (var path in candidates.Skip(effective))
        {
            File.Delete(path);
            removed.Add(Path.GetFileName(path));
        }

        return removed;
    }

    /// <summary>文件名是不是"本工具产出的备份"（严格按形态判，避免误删别人放这儿的东西）。</summary>
    private static bool IsBackupName(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(FileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var core = fileName[FilePrefix.Length..^FileExtension.Length];
        var separator = core.IndexOf('-', StringComparison.Ordinal);
        var stamp = separator < 0 ? core : core[..separator];
        var suffix = separator < 0 ? string.Empty : core[(separator + 1)..];
        if (suffix.Length > 0 && !suffix.All(char.IsAsciiDigit))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            stamp,
            StampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
    }

    /// <summary>在 Unix 上把备份收成"只有属主可读"（库文件里是全部口令哈希与整局事件流）。</summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
