using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 库的运维底座（M5 / G-A6-4 · G-A6-6 · G-A6-8）：连接口径真的生效、热备份真的能用、轮转不误伤。
/// </summary>
/// <remarks>
/// <para>
/// 这几条判据全部落在**真宿主 + 真 SQLite 文件**上：审计里那两条读数（<c>journal_mode</c> 靠人工设、
/// <c>busy_timeout</c> 无人声明）都不是"看代码能看出来"的问题，只有对着真连接读 PRAGMA 才算证明。
/// </para>
/// <para>
/// 备份这一族刻意**不停服**：备份是在宿主还开着的时候做的，做的还是宿主正在写的那个库文件——
/// 这正是生产里定时备份的形态。
/// </para>
/// </remarks>
public sealed class DatabaseMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oct-maintenance-{Guid.NewGuid():N}");

    /// <summary>构造用例：建一个临时根目录，备份与恢复都在它下面做。</summary>
    public DatabaseMaintenanceTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// 口径生效（G-A6-8）：宿主起来之后，**从 EF 的打开路径**拿到的连接上，
    /// 日志模式是 wal、忙等是配置值、同步级别是 FULL——而且**已经躺在磁盘上的库也要被设回来**。
    /// </summary>
    /// <remarks>
    /// 为什么刻意从"已存在的库"起手：EF Core 的建库路径自己就把新库设成 WAL（实测：裸连接建库 = delete，
    /// 经 <c>EnsureCreated</c> 建库 = wal），所以在全新库上断言 wal **证明不了本项目的口径**。
    /// 真正的缺口是磁盘上已有的库——从备份恢复回来的库正是 delete 形态（备份刻意收成单文件 rollback 模式），
    /// 旧版本或手工建的库也可能是 delete。这条用例先把它打回 delete 再启动，看启动引导是否按口径设回来。
    /// </remarks>
    [Fact]
    public async Task HostStart_SetsJournalModeAndBusyTimeoutOnAnExistingDatabase()
    {
        var databasePath = Path.Combine(_root, "existing.db");
        await using (var seeding = new TestServerHost(
            databasePath: databasePath,
            deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(databasePath));
        }

        SqliteConnection.ClearAllPools();
        await using (var bare = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await bare.OpenAsync();
            await using var command = bare.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=delete;";
            Assert.Equal("delete", Convert.ToString(await command.ExecuteScalarAsync()));
        }

        await using var host = new TestServerHost(
            databasePath: databasePath,
            deleteDatabaseOnDispose: false);
        var factory = host.Services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();

        // journal_mode 是库文件里的持久属性，连接级读回即可。
        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
        // 忙等与同步级别是连接级属性：拦截器没生效的话这里会是 0 与 2 的框架默认值。
        Assert.Equal(5000L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA busy_timeout;")));
        Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA synchronous;")));
    }

    /// <summary>
    /// 热备份可用（G-A6-4）：宿主开着的时候备一份，备出来的库**完整性 ok、内容与源库一致**。
    /// </summary>
    [Fact]
    public async Task Backup_OfRunningHost_IsCompleteAndReadable()
    {
        await using var host = new TestServerHost();
        var backupDirectory = Path.Combine(_root, "backups");
        var options = new SqliteOptions();
        using var output = new StringWriter();

        var backup = DatabaseBackup.Write(host.DatabasePath, backupDirectory, DatabaseBackup.DefaultKeep, options, output);
        var source = DatabaseInspector.Inspect(host.DatabasePath, options);

        Assert.Equal("ok", backup.Integrity);
        Assert.True(backup.SizeBytes > 0);
        Assert.Equal(64, backup.Sha256.Length);
        Assert.Equal(source.Tables.Games, backup.Tables.Games);
        Assert.Equal(source.Tables.Events, backup.Tables.Events);
        Assert.Equal(source.Tables.Users, backup.Tables.Users);
        Assert.Equal(source.Tables.SeatBindings, backup.Tables.SeatBindings);
        Assert.Equal(
            source.Games.Select(game => (game.GameId, game.Events, game.LastSequence, game.PayloadBytes)),
            backup.Games.Select(game => (game.GameId, game.Events, game.LastSequence, game.PayloadBytes)));
        // 事件流真的有内容：空库上比"两边都是 0"证明不了任何事。
        Assert.True(backup.Tables.Events > 0, "夹具该造出事件来，否则这条判据是空转的");
        Assert.NotEqual(source.Sha256, backup.Sha256);
        // 输出里要有"给人看的读数"：运维只看 journal，不看返回对象。
        var log = output.ToString();
        Assert.Contains("完整性=ok", log, StringComparison.Ordinal);
        Assert.Contains("轮转：保留最近", log, StringComparison.Ordinal);
    }

    /// <summary>备份**绝不覆盖**：同一瞬间备两次会得到两个文件（时间戳相同就追加序号）。</summary>
    [Fact]
    public async Task Backup_TwiceWithoutDelay_WritesTwoFilesAndKeepsBoth()
    {
        await using var host = new TestServerHost();
        var backupDirectory = Path.Combine(_root, "backups");
        var options = new SqliteOptions();
        using var output = new StringWriter();

        var first = DatabaseBackup.Write(host.DatabasePath, backupDirectory, 7, options, output);
        var second = DatabaseBackup.Write(host.DatabasePath, backupDirectory, 7, options, output);

        Assert.NotEqual(first.Path, second.Path);
        Assert.True(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
        Assert.Equal(2, Directory.GetFiles(backupDirectory, "oct-*.db").Length);
    }

    /// <summary>
    /// 轮转只认自己产出的名字（G-A6-4）：保留最近 N 份，手里别的东西（人工快照、随手放的说明）一律不碰。
    /// </summary>
    [Fact]
    public async Task Backup_Rotation_KeepsNewestAndLeavesForeignFilesAlone()
    {
        await using var host = new TestServerHost();
        var backupDirectory = Path.Combine(_root, "backups");
        Directory.CreateDirectory(backupDirectory);
        var options = new SqliteOptions();
        using var output = new StringWriter();

        string[] oldBackups =
        [
            "oct-20200101T000000Z.db",
            "oct-20200102T000000Z.db",
            "oct-20200103T000000Z.db",
        ];
        foreach (var name in oldBackups)
        {
            await File.WriteAllTextAsync(Path.Combine(backupDirectory, name), "old");
        }

        var foreign = Path.Combine(backupDirectory, "manual-snapshot.db");
        // 这一份长得像备份（`oct-*.db`）、但不是本工具产出的那种名字：轮转必须认得出它不是自己的。
        var lookAlike = Path.Combine(backupDirectory, "oct-20200101-manual-copy.db");
        var note = Path.Combine(backupDirectory, "README.txt");
        await File.WriteAllTextAsync(foreign, "别人的快照");
        await File.WriteAllTextAsync(lookAlike, "手工复制的旧备份");
        await File.WriteAllTextAsync(note, "说明");

        _ = DatabaseBackup.Write(host.DatabasePath, backupDirectory, 2, options, output);

        var remaining = Directory.GetFiles(backupDirectory, "oct-*.db").Select(Path.GetFileName).OrderBy(name => name).ToList();
        Assert.Equal(3, remaining.Count);
        // 最新的那两份：本次备份 + 2020 年那批里最靠后的一个；剩下的那个是"像备份但不是"的那份。
        Assert.Contains("oct-20200103T000000Z.db", remaining);
        Assert.Contains("oct-20200101-manual-copy.db", remaining);
        Assert.DoesNotContain("oct-20200101T000000Z.db", remaining);
        Assert.True(File.Exists(foreign), "轮转不许碰不是本工具产出的文件");
        Assert.True(File.Exists(note));
    }

    /// <summary>
    /// 坏备份留不下来（G-A6-4）：把备份内容改坏之后，体检必须报出问题——
    /// "看起来有、其实是坏的"备份比没有备份更危险。
    /// </summary>
    [Fact]
    public async Task Inspect_OnCorruptedCopy_ReportsNotOk()
    {
        await using var host = new TestServerHost();
        var options = new SqliteOptions();
        using var output = new StringWriter();
        var backup = DatabaseBackup.Write(host.DatabasePath, Path.Combine(_root, "backups"), 7, options, output);

        // 在第一个 b-tree 页里灌一段垃圾：页内的单元指针数组被破坏，integrity_check 必须报出来。
        var garbage = new byte[128];
        Array.Fill(garbage, (byte)0xFF);
        await using (var stream = new FileStream(backup.Path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Seek(backup.PageSize, SeekOrigin.Begin);
            await stream.WriteAsync(garbage);
            await stream.FlushAsync();
        }

        var corrupted = DatabaseInspector.Inspect(backup.Path, options);
        Assert.NotEqual("ok", corrupted.Integrity);
    }

    /// <summary>拿一个根本不是库的文件去体检时，不许假装成功——要报错、要有话可说。</summary>
    [Fact]
    public async Task Inspect_OnFileThatIsNotADatabase_Fails()
    {
        var notADatabase = Path.Combine(_root, "not-a-database.db");
        await File.WriteAllTextAsync(notADatabase, "这不是一个 SQLite 文件");

        var exception = Record.Exception(() => DatabaseInspector.Inspect(notADatabase, new SqliteOptions()));
        Assert.NotNull(exception);
        Assert.IsType<SqliteException>(exception);
    }

    /// <summary>库文件不存在时，维护命令给的是"路径不对"而不是一段栈。</summary>
    [Fact]
    public void Cli_WithoutDatabaseFile_Fails_WithReadableMessage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = MaintenanceCli.Run(
            ["backup", "--out", Path.Combine(_root, "backups"), "--db", Path.Combine(_root, "missing.db")],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("数据库不存在", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 恢复演练的**最小复现**（G-A6-4）：备份 → 体检那份备份 → 里面的某一局与事件条数读得出来。
    /// </summary>
    /// <remarks>
    /// 完整演练（把备份放回 <c>oct.db</c>、起服、页面 200）在真机上跑，读数记在批次里；
    /// 这条用例守的是同一套判据的"每次提交都跑"的那一半。
    /// </remarks>
    [Fact]
    public async Task Backup_CanBeReadBackAsAGameHistory()
    {
        await using var host = new TestServerHost();
        var backupDirectory = Path.Combine(_root, "backups");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var backupExitCode = MaintenanceCli.Run(
            ["backup", "--out", backupDirectory, "--db", host.DatabasePath],
            output,
            error);
        Assert.Equal(0, backupExitCode);

        var backupFile = Assert.Single(Directory.GetFiles(backupDirectory, "oct-*.db"));
        using var reportOutput = new StringWriter();
        var reportExitCode = MaintenanceCli.Run(["db-report", "--db", backupFile], reportOutput, error);

        Assert.Equal(0, reportExitCode);
        var text = reportOutput.ToString();
        Assert.Contains("完整性=ok", text, StringComparison.Ordinal);
        Assert.Contains($"标识={TestServerHost.GameId.Value}", text, StringComparison.Ordinal);
        Assert.Contains("事件=", text, StringComparison.Ordinal);
    }

    /// <summary>备份文件在 Unix 上只有属主可读（G-A6-6：里面是全部口令哈希与整局事件流）。</summary>
    [Fact]
    public async Task BackupFile_OnUnix_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var host = new TestServerHost();
        using var output = new StringWriter();
        var backup = DatabaseBackup.Write(host.DatabasePath, Path.Combine(_root, "backups"), 7, new SqliteOptions(), output);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(backup.Path));
    }

    /// <summary>
    /// 体积读数（M5 / G-A6-5）：体检要给出"事件载荷多少字节、平均每条多少"，容量才有依据。
    /// </summary>
    /// <remarks>
    /// 审计的原话是"库层无任何体积估算"，于是"再放半年会不会撑爆磁盘"只能靠猜。
    /// 这条判据同时钉住两件事：**量的是字节不是字符**（中文载荷一个字三字节，按字符数量会低估三倍），
    /// 以及那一行真的出现在给人看的输出里（运维只看 journal，不看返回对象）。
    /// </remarks>
    [Fact]
    public async Task Report_ShowsVolumeReadingAsCapacityEvidence()
    {
        await using var host = new TestServerHost();
        var report = DatabaseInspector.Inspect(host.DatabasePath, new SqliteOptions());

        Assert.True(report.Tables.Events > 0, "夹具该造出事件来，否则这条判据是空转的");
        Assert.True(report.EventPayloadBytes > 0, "事件载荷字节数必须是真量出来的");
        Assert.True(report.BytesPerEvent > 0);
        // 每一个字符都是 ASCII 或中文：载荷字节数必然**不小于**条数（一条事件至少一个字节）。
        Assert.True(report.EventPayloadBytes >= report.Tables.Events);

        var text = report.Describe();
        Assert.Contains("体积：事件载荷=", text, StringComparison.Ordinal);
        Assert.Contains("平均每事件", text, StringComparison.Ordinal);
        Assert.Contains("载荷=", text, StringComparison.Ordinal);
        Assert.Contains("最重一局=", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 空闲桌回收命令（M5 / G-A6-5）：默认**只报告**，加 <c>--apply</c> 才真的删，删完五张表都干净。
    /// </summary>
    /// <remarks>
    /// 停机状态下跑（<c>--apply</c> 要拿单实例锁）：这条路径正是"运维手工清理"的替代品，
    /// 它必须能在**没有服务**的时候把一张桌删干净，否则运维还得回到手工五表联删的老路。
    /// </remarks>
    [Fact]
    public async Task RetireTables_DryRunReports_ApplyDeletes()
    {
        var databasePath = Path.Combine(_root, "retire.db");
        await using (var seeded = new TestServerHost(databasePath: databasePath, deleteDatabaseOnDispose: false))
        {
            await seeded.RegisterTableAsync(new OpenClockTower.Application.GameId("doomed"), 5);
        }

        SqliteConnection.ClearAllPools();
        BackdateTable(databasePath, "doomed", DateTimeOffset.UtcNow.AddDays(-2));

        using var output = new StringWriter();
        using var error = new StringWriter();

        // 默认档：只报告，一个字节都不动。
        Assert.Equal(0, MaintenanceCli.Run(["retire-tables", "--db", databasePath], output, error));
        Assert.Contains("模式=只报告", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("标识=doomed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, CountGames(databasePath, "doomed"));

        // --apply：真的删，且五张表都不再留着它的行。
        using var applyOutput = new StringWriter();
        Assert.Equal(
            0,
            MaintenanceCli.Run(
                ["retire-tables", "--apply", "--empty-hours", "0", "--db", databasePath],
                applyOutput,
                error));
        Assert.Contains("已回收=1", applyOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, CountGames(databasePath, "doomed"));
        foreach (var table in new[] { "Events", "Snapshots", "Receipts", "SeatBindings" })
        {
            Assert.Equal(0, CountRows(databasePath, table, "doomed"));
        }
    }

    /// <summary>
    /// <c>--apply</c> 在服务跑着的时候**拿不到锁**：两个进程同改一个库是必须挡住的事，
    /// 而"服务跑着时它会自己回收"正是给运维的那句人话。
    /// </summary>
    [Fact]
    public async Task RetireTables_ApplyIsRefusedWhileTheServiceHoldsTheLock()
    {
        await using var host = new TestServerHost();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = MaintenanceCli.Run(["retire-tables", "--apply", "--db", host.DatabasePath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("服务正在运行", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("停服", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<object?> ScalarAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    /// <summary>把一桌的建桌时刻改到过去（"这一桌空了多久"是时间这一维的输入，夹具只能直接改库）。</summary>
    private static void BackdateTable(string databasePath, string gameId, DateTimeOffset createdAt)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET CreatedAt = $when WHERE GameId = $id;";
        command.Parameters.AddWithValue("$when", createdAt);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    private static long CountGames(string databasePath, string gameId) =>
        CountRows(databasePath, "Games", gameId);

    private static long CountRows(string databasePath, string table, string gameId)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // 连接池会继续握着库文件句柄：先清池，再按"逐层列文件 → 逐个删 → 回看"的纪律清临时产物。
        SqliteConnection.ClearAllPools();
        var backups = Path.Combine(_root, "backups");
        var pending = new List<string> { _root, backups };

        // 先删文件（两层：临时根 / backups），再自下而上删空目录——不做递归删除。
        foreach (var directory in pending)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                TryDeleteFile(file);
            }
        }

        foreach (var directory in Enumerable.Reverse(pending))
        {
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 仍被占用时留下文件，由 %TEMP% 收尾统一清理；这里不把"删不掉"变成用例失败。
        }
    }
}
