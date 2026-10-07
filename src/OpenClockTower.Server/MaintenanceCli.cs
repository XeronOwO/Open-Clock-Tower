using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 数据库维护命令的命令行入口（M5 / G-A6-4 · G-A6-5）：
/// <c>backup</c>（热备份）· <c>db-report</c>（只读体检）· <c>retire-tables</c>（空闲桌回收）。
/// </summary>
/// <remarks>
/// <para>
/// **刻意不进 Web 宿主**：这些命令要在服务正常运行时被 systemd 定时器直接调用，
/// 起来一个 Kestrel、占住端口、把 SignalR 也拉起来是完全没有必要的（而且会与正在跑的那个实例打架）。
/// 因此 <c>Program.cs</c> 在 <c>WebApplication.CreateBuilder</c> **之前**就把它们分流出去。
/// </para>
/// <para>
/// **只读命令不拿单实例锁、写命令拿**（M5 / D-0036）：<c>backup</c> / <c>db-report</c> /
/// <c>retire-tables</c> 的只读档在服务跑着的时候本来就该能做；而 <c>retire-tables --apply</c>
/// 会删数据，必须独占——服务在跑时它连锁都拿不到，当场报"另一个实例正在使用这个库"。
/// 这条口径把"两个进程同时改库"从"小心别撞"变成了"撞不上"。
/// </para>
/// <para>
/// 退出码是给定时器看的：0 = 成功，1 = 失败（参数不全 / 库不存在 / 拿不到锁）。
/// 输出是给人看的（一行一件事），systemd 会把它收进 journal。
/// </para>
/// </remarks>
public static class MaintenanceCli
{
    /// <summary>热备份命令。</summary>
    public const string BackupCommand = "backup";

    /// <summary>只读体检命令。</summary>
    public const string ReportCommand = "db-report";

    /// <summary>空闲桌回收命令（默认只报告，<c>--apply</c> 才真的删）。</summary>
    public const string RetireTablesCommand = "retire-tables";

    /// <summary>第一个参数是不是维护命令（不是就当普通启动，交给 Web 宿主）。</summary>
    public static bool IsMaintenanceCommand(string[] args) =>
        args.Length > 0 && args[0] is BackupCommand or ReportCommand or RetireTablesCommand;

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
                RetireTablesCommand => RunRetireTables(flags, databasePath, sqliteOptions, output),
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
        + "      OpenClockTower.Server retire-tables [--apply] [--empty-hours N] [--played-days N] [--db <库路径>]"
        + Environment.NewLine
        + "不带参数启动 = 正常起服务；库路径缺省取配置 GameServer:DatabasePath。" + Environment.NewLine
        + "retire-tables 默认**只报告**；--apply 才真的删（它要求没有服务在跑：会去拿单实例锁）。";

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

    /// <summary>
    /// 空闲桌回收（M5 / G-A6-5）：与宿主里那个定时清扫**同一个清扫器**，这里只是换了个触发方式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认只报告：回收不可逆，人肉命令不该一敲就删。要真删必须显式 <c>--apply</c>。
    /// </para>
    /// <para>
    /// 阈值可以用 <c>--empty-hours</c> / <c>--played-days</c> 覆盖（只影响这一次运行）：
    /// 演练与"如果我把期限改成 30 天会删掉哪些桌"都靠它，不必先去改服务配置再重启。
    /// </para>
    /// </remarks>
    private static int RunRetireTables(
        IReadOnlyDictionary<string, string> flags,
        string databasePath,
        SqliteOptions sqliteOptions,
        TextWriter output)
    {
        var apply = flags.ContainsKey("--apply");
        var retention = new TableRetentionOptions();
        if (ReadNonNegative(flags, "--empty-hours") is { } emptyHours)
        {
            retention.EmptyTableHours = emptyHours;
        }

        if (ReadNonNegative(flags, "--played-days") is { } playedDays)
        {
            retention.PlayedTableDays = playedDays;
        }

        // 写操作必须独占（见类注释）：拿不到锁说明服务正在跑，那时该用定时清扫或等它自己回收。
        using var instanceLock = apply
            ? AcquireForWrite(databasePath)
            : null;

        var contextOptions = new DbContextOptionsBuilder<GameDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .AddInterceptors(new SqlitePragmaInterceptor(sqliteOptions))
            .Options;

        try
        {
            using var factory = new MaintenanceDbContextFactory(contextOptions);
            var service = new TableRetirementService(
                new EfTableRetirementStore(factory),
                new TableRetirementPolicy(Options.Create(retention)),
                // 维护进程里没有服务的内存连接表：一桌也没人在用（服务没在跑，这正是锁的意义）。
                new ConnectionRegistry(),
                new OfflineTableUnloader(),
                new SystemClock(),
                Options.Create(retention),
                NullLogger<TableRetirementService>.Instance);

            output.WriteLine(
                $"回收口径：未开局桌保留 {retention.EmptyTableHours} 小时 · 开过局桌保留 {retention.PlayedTableDays} 天"
                + $" · 模式={(apply ? "执行（--apply）" : "只报告")}");

            var report = service.SweepAsync(apply, CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine(report.Describe());

            if (!apply && report.DueCount > 0)
            {
                output.WriteLine($"要真的回收这 {report.DueCount} 张桌，加 --apply 再跑一次。");
            }

            return 0;
        }
        finally
        {
            // 短命进程：别把库文件句柄留在池里（Windows 上还牵着"这个文件能不能被删/被移"）。
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>拿单实例锁（写操作的独占前提）；拿不到时给一句针对维护场景的话。</summary>
    private static ServerInstanceLock AcquireForWrite(string databasePath)
    {
        try
        {
            return ServerInstanceLock.Acquire(databasePath, NullLogger.Instance);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"回收要独占这个库，但服务正在运行：{ServerInstanceLock.PathFor(databasePath)} 被它拿着。"
                + "服务跑着的时候它会按保留期自己回收（不用你动手）；要手工回收就先停服，"
                + "或者对一份**备份副本**跑（--db 指向副本即可，那条路径不与服务共享锁文件）。",
                exception);
        }
    }

    /// <summary>读一个非负整数开关；没给返回 null（保持 <see cref="TableRetentionOptions"/> 的默认值）。</summary>
    private static int? ReadNonNegative(IReadOnlyDictionary<string, string> flags, string name)
    {
        if (!flags.TryGetValue(name, out var text))
        {
            return null;
        }

        if (!int.TryParse(text, out var value) || value < 0)
        {
            throw new InvalidOperationException($"{name} 必须是 ≥ 0 的整数：当前是 {text}");
        }

        return value;
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

    /// <summary>没有值的开关（写全名单：多一个就多一种"少给参数也能跑"的可能）。</summary>
    private static readonly HashSet<string> ValuelessFlags = new(["--apply"], StringComparer.Ordinal);

    /// <summary>只认 <c>--名字 值</c> 与名单里的 <c>--名字</c> 两种形态；认不出来的参数当场报错，不静默忽略。</summary>
    private static Dictionary<string, string> ReadFlags(IEnumerable<string> arguments)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>(arguments);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"参数不认识：{name}{Environment.NewLine}{Usage}");
            }

            if (ValuelessFlags.Contains(name))
            {
                flags[name] = string.Empty;
                continue;
            }

            if (queue.Count == 0)
            {
                throw new InvalidOperationException($"参数 {name} 后面缺一个值。{Environment.NewLine}{Usage}");
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

    /// <summary>
    /// 维护命令自己的上下文工厂：一次调用一个上下文，**不池化**。
    /// </summary>
    /// <remarks>
    /// 短命进程用不上池化，而池化会让"命令跑完了，文件句柄还在池里"变成一件要额外解释的事
    /// （体检与备份刻意都用 <c>Pooling=False</c>，同一条理由：跑完就要能把文件交给别人）。
    /// </remarks>
    private sealed class MaintenanceDbContextFactory : IDbContextFactory<GameDbContext>, IDisposable
    {
        private readonly DbContextOptions<GameDbContext> _options;

        internal MaintenanceDbContextFactory(DbContextOptions<GameDbContext> options) => _options = options;

        /// <inheritdoc />
        public GameDbContext CreateDbContext() => new(_options);

        /// <inheritdoc />
        public Task<GameDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        /// <inheritdoc />
        public void Dispose()
        {
            // 没有池要清：实现 IDisposable 是为了让 using 的意图成立（将来若改成池化，这里就是清池的地方）。
        }
    }

    /// <summary>
    /// 维护进程里的"摘表"：**没有内存注册表**，所以一桌也摘不掉（返回 false）。
    /// </summary>
    /// <remarks>
    /// 这是显式的空实现，不是偷懒：本进程不会装载任何桌（服务没在跑——它连单实例锁都拿不到），
    /// 于是"摘表"在这里按定义无事可做。有了它，维护命令与宿主用的是**同一个清扫器**，
    /// 判定与删除不会长出第二套口径。
    /// </remarks>
    private sealed class OfflineTableUnloader : ITableUnloader
    {
        /// <inheritdoc />
        public bool Unload(GameId gameId) => false;
    }
}
