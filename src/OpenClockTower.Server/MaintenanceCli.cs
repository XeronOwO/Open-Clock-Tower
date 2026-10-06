using Microsoft.Extensions.Configuration;

namespace OpenClockTower.Server;

/// <summary>
/// 数据库维护命令的命令行入口（M5 / G-A6-4）：<c>backup</c>（热备份）与 <c>db-report</c>（只读体检）。
/// </summary>
/// <remarks>
/// <para>
/// **刻意不进 Web 宿主**：这两个命令要在服务正常运行时被 systemd 定时器直接调用，
/// 起来一个 Kestrel、占住端口、把 SignalR 也拉起来是完全没有必要的（而且会与正在跑的那个实例打架）。
/// 因此 <c>Program.cs</c> 在 <c>WebApplication.CreateBuilder</c> **之前**就把它们分流出去。
/// </para>
/// <para>
/// 退出码是给定时器看的：0 = 成功，1 = 失败（参数不全 / 库不存在 / 完整性没过）。
/// 输出是给人看的（一行一件事），systemd 会把它收进 journal。
/// </para>
/// </remarks>
public static class MaintenanceCli
{
    /// <summary>热备份命令。</summary>
    public const string BackupCommand = "backup";

    /// <summary>只读体检命令。</summary>
    public const string ReportCommand = "db-report";

    /// <summary>第一个参数是不是维护命令（不是就当普通启动，交给 Web 宿主）。</summary>
    public static bool IsMaintenanceCommand(string[] args) =>
        args.Length > 0 && args[0] is BackupCommand or ReportCommand;

    /// <summary>跑一个维护命令，返回进程退出码。</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var configuration = BuildConfiguration();
            var flags = ReadFlags(args.Skip(1));
            var databasePath = ResolveDatabasePath(flags, configuration);
            var sqliteOptions = configuration.GetSection(SqliteOptions.SectionName).Get<SqliteOptions>()
                                ?? new SqliteOptions();

            return args[0] switch
            {
                BackupCommand => RunBackup(flags, databasePath, sqliteOptions, output),
                ReportCommand => RunReport(databasePath, sqliteOptions, output, error),
                _ => Fail(error, $"未知的维护命令：{args[0]}{Environment.NewLine}{Usage}"),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error.WriteLine($"失败：{exception.Message}");
            return 1;
        }
    }

    /// <summary>用法说明（参数不全时打给运维看）。</summary>
    public static string Usage =>
        "用法：OpenClockTower.Server backup --out <目录> [--keep 7] [--db <库路径>]" + Environment.NewLine
        + "      OpenClockTower.Server db-report [--db <库路径>]" + Environment.NewLine
        + "不带参数启动 = 正常起服务；库路径缺省取配置 GameServer:DatabasePath。";

    private static int RunBackup(
        IReadOnlyDictionary<string, string> flags,
        string databasePath,
        SqliteOptions sqliteOptions,
        TextWriter output)
    {
        if (!flags.TryGetValue("--out", out var outputDirectory) || string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"backup 必须给输出目录。{Environment.NewLine}{Usage}");
        }

        var keep = DatabaseBackup.DefaultKeep;
        if (flags.TryGetValue("--keep", out var keepText)
            && (!int.TryParse(keepText, out keep) || keep < 1))
        {
            throw new InvalidOperationException($"--keep 必须是 ≥ 1 的整数：当前是 {keepText}");
        }

        var report = DatabaseBackup.Write(databasePath, outputDirectory, keep, sqliteOptions, output);
        output.WriteLine($"备份可用：{report.Path}");
        return 0;
    }

    private static int RunReport(
        string databasePath,
        SqliteOptions sqliteOptions,
        TextWriter output,
        TextWriter error)
    {
        var report = DatabaseInspector.Inspect(databasePath, sqliteOptions);
        output.WriteLine(report.Describe());
        if (!string.Equals(report.Integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"体检未通过：完整性={report.Integrity}——这个库不能拿来做恢复。");
            return 1;
        }

        return 0;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }

    /// <summary>
    /// 从命令行、环境变量与 <c>appsettings.json</c> 里取库路径：显式参数优先，
    /// 其次是配置（systemd 单元用 <c>GameServer__DatabasePath</c> 下发的就是它）。
    /// </summary>
    private static string ResolveDatabasePath(
        IReadOnlyDictionary<string, string> flags,
        IConfigurationRoot configuration)
    {
        if (flags.TryGetValue("--db", out var explicitPath) && !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var configured = configuration["GameServer:DatabasePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        throw new InvalidOperationException(
            $"没有库路径：用 --db <路径> 指定，或在配置里写 GameServer:DatabasePath。{Environment.NewLine}{Usage}");
    }

    /// <summary>只认 <c>--名字 值</c> 这一种形态；认不出来的参数当场报错，不静默忽略。</summary>
    private static Dictionary<string, string> ReadFlags(IEnumerable<string> arguments)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>(arguments);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!name.StartsWith("--", StringComparison.Ordinal) || queue.Count == 0)
            {
                throw new InvalidOperationException($"参数不认识：{name}{Environment.NewLine}{Usage}");
            }

            flags[name] = queue.Dequeue();
        }

        return flags;
    }

    /// <summary>
    /// 维护命令用的配置（与宿主同一套来源：工作目录下的 <c>appsettings.json</c> + 环境变量）。
    /// </summary>
    /// <remarks>
    /// 刻意不启动宿主、不读命令行参数里的配置项：定时器是把命令写死在单元文件里的，
    /// 配置来源越多，"定时备份备的是哪个库"就越难回答。
    /// </remarks>
    private static IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
}
