using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 空闲桌回收在**真宿主 + 真库**上的判据（M5 / G-A5-5 容量半边 · G-A6-5）。
/// </summary>
/// <remarks>
/// <para>
/// 判定那一半在 <see cref="TableRetirementTests"/>（纯计算）；这里判的是**动作**：
/// 五张表真的删干净了没有、注册表摘了没有、大厅里还在不在、该不动的一张有没有被牵连。
/// 回收不可逆，"删干净"与"不该删的没删"两条都必须有运行时证据，不能只看命令没报错。
/// </para>
/// <para>
/// 桌的"最后活跃时刻"由夹具直接改库（<c>UPDATE</c>）造出来——那是**时间**这一维的输入，
/// 不是产品判定：真机上它来自真实的时钟，测试里没有别的办法让一张桌"看起来是三天前开的"。
/// </para>
/// </remarks>
public sealed class TableRetirementHostTests
{
    /// <summary>判"这一桌在库里还有没有行"时要看全的五张表（漏一张就等于没判）。</summary>
    private static readonly string[] GameTables =
        ["Events", "Snapshots", "Receipts", "Games", "SeatBindings"];

    /// <summary>
    /// 没人动过的空桌到期之后：**五张表都删干净**、注册表摘掉、大厅里消失，而别的桌一点都不动。
    /// </summary>
    [Fact]
    public async Task IdleEmptyTable_IsRetired_AndErasedFromEveryTable()
    {
        await using var host = new TestServerHost();
        var idle = new GameId("idle-table");
        await host.RegisterTableAsync(idle, 5);
        BackdateCreatedAt(host.DatabasePath, idle, DateTimeOffset.UtcNow.AddDays(-2));

        var report = await SweepAsync(host);

        var outcome = Assert.Single(report.Outcomes, item => item.GameId == idle);
        Assert.Equal(TableRetirementVerdict.Retire, outcome.Verdict);
        Assert.NotNull(outcome.Purged);
        Assert.Equal(1, outcome.Purged!.Games);
        Assert.Equal(1, report.RetiredCount);
        Assert.Equal(0, report.UndecidableCount);

        Assert.False(host.GameRegistry.Contains(idle), "回收要把这一桌从注册表里摘掉（不然它还在被心跳推进）");
        foreach (var table in GameTables)
        {
            Assert.Equal(0, CountRows(host.DatabasePath, table, idle.Value));
        }

        // 大厅里也看不到了（那一桌从"在册"这个事实里消失了）。
        var lobby = await host.Services
            .GetRequiredService<LobbyService>()
            .ListAsync(null, CancellationToken.None);
        Assert.DoesNotContain(lobby, item => item.GameId == idle.Value);

        // 反方向：默认桌刚开过局、还在保留期内，一个字节都不该动。
        Assert.Contains(lobby, item => item.GameId == TestServerHost.GameId.Value);
        Assert.True(host.GameRegistry.Contains(TestServerHost.GameId));
    }

    /// <summary>
    /// **开过局**的桌走天级那一档：把整局事件流推迟 100 天之后才到期。
    /// </summary>
    /// <remarks>
    /// 这条同时证明"活跃度取事件时刻"这条路真的接到了库里——只按建桌时刻算的话，
    /// 下面那次 <c>-100 天</c> 的建桌时刻就够了，而这里判的是**事件**这一维。
    /// </remarks>
    [Fact]
    public async Task PlayedTable_IsRetiredOnlyAfterTheDayThreshold()
    {
        await using var host = new TestServerHost();
        var stale = new GameId("stale-game");
        await host.RegisterTableAsync(stale, 5);
        await GiveItAnEventAsync(host, stale, 5);

        // 30 天：还没到 90 天，一条都不许删。
        Backdate(host.DatabasePath, stale, DateTimeOffset.UtcNow.AddDays(-30));
        var recent = await SweepAsync(host);
        Assert.Equal(TableRetirementVerdict.WithinRetention, Assert.Single(recent.Outcomes, item => item.GameId == stale).Verdict);
        Assert.True(host.GameRegistry.Contains(stale));

        // 100 天：到期，回收。
        Backdate(host.DatabasePath, stale, DateTimeOffset.UtcNow.AddDays(-100));
        var report = await SweepAsync(host);
        var outcome = Assert.Single(report.Outcomes, item => item.GameId == stale);
        Assert.Equal(TableRetirementVerdict.Retire, outcome.Verdict);
        Assert.Contains("已开局", outcome.Reason, StringComparison.Ordinal);
        Assert.True(outcome.Purged!.Events > 0, "开过局的桌回收时应当真的删掉了事件（判据不能是空转的）");
        Assert.Equal(0, CountRows(host.DatabasePath, "Games", stale.Value));
    }

    /// <summary>有人在线的桌一律不动：哪怕它的最后一条事件在一年前。</summary>
    [Fact]
    public async Task TableWithLiveConnection_IsSkipped()
    {
        await using var host = new TestServerHost();
        await using var storyteller = await host.ConnectStorytellerAsync();
        Backdate(host.DatabasePath, TestServerHost.GameId, DateTimeOffset.UtcNow.AddDays(-400));

        var report = await SweepAsync(host);

        var outcome = Assert.Single(report.Outcomes, item => item.GameId == TestServerHost.GameId);
        Assert.Equal(TableRetirementVerdict.InUse, outcome.Verdict);
        Assert.Null(outcome.Purged);
        Assert.Equal(0, report.RetiredCount);
        Assert.Equal(1, report.InUseCount);
        Assert.True(host.GameRegistry.Contains(TestServerHost.GameId), "跳到在线桌 = 把一局正在打的牌删掉");
        Assert.True(CountRows(host.DatabasePath, "Games", TestServerHost.GameId.Value) > 0);
    }

    /// <summary>只报告不执行（<c>retire-tables</c> 的默认档）：判定照给，库一个字节都不动。</summary>
    [Fact]
    public async Task DryRun_ReportsWithoutDeleting()
    {
        await using var host = new TestServerHost();
        Backdate(host.DatabasePath, TestServerHost.GameId, DateTimeOffset.UtcNow.AddDays(-400));

        var report = await host.Services
            .GetRequiredService<TableRetirementService>()
            .SweepAsync(apply: false, CancellationToken.None);

        Assert.True(report.DueCount > 0, "夹具该造出一张到期的桌，否则这条判据是空转的");
        Assert.Equal(0, report.RetiredCount);
        Assert.True(host.GameRegistry.Contains(TestServerHost.GameId));
        Assert.True(CountRows(host.DatabasePath, "Games", TestServerHost.GameId.Value) > 0);
    }

    /// <summary>
    /// 顺手清掉**孤儿行**：属于不存在的桌的事件 / 快照 / 回执 / 绑定在任何时候都是残渣
    /// （历史上有过"运维手工五表联删"的年代，漏一张表就会留下它们），而只有回收知道"哪些桌存在"。
    /// </summary>
    [Fact]
    public async Task OrphanRows_AreSweptAwayTogether()
    {
        await using var host = new TestServerHost();
        var idle = new GameId("orphans");
        await host.RegisterTableAsync(idle, 5);
        BackdateCreatedAt(host.DatabasePath, idle, DateTimeOffset.UtcNow.AddDays(-2));

        InsertOrphanRows(host.DatabasePath, "already-deleted");

        var report = await SweepAsync(host);

        var outcome = Assert.Single(report.Outcomes, item => item.GameId == idle);
        Assert.True(outcome.Purged!.OrphanRows > 0);
        foreach (var table in GameTables)
        {
            Assert.Equal(0, CountRows(host.DatabasePath, table, "already-deleted"));
        }
    }

    /// <summary>新开的桌会写下**建桌时刻**（结构 v2 的写入路径）：没有它，空桌与老桌就分不出来。</summary>
    [Fact]
    public async Task NewTable_RecordsItsCreationTime()
    {
        await using var host = new TestServerHost();
        var created = new GameId("stamped");
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        await host.RegisterTableAsync(created, 5);

        var stored = ReadCreatedAt(host.DatabasePath, created);

        Assert.NotNull(stored);
        Assert.InRange(stored!.Value, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private static async Task<TableRetirementReport> SweepAsync(TestServerHost host) =>
        await host.Services.GetRequiredService<TableRetirementService>()
            .SweepAsync(apply: true, CancellationToken.None);

    /// <summary>
    /// 给一桌造一条**真实事件**：让这一局的会话跑一次开阶段（走真内核与真存储，不手工插行）。
    /// </summary>
    /// <remarks>
    /// 不插假事件：这条用例要判的是"开过局的桌走天级那一档"，而"开过局"的定义就是**事件条数 &gt; 0**——
    /// 拿手工插的行去喂它，判的就只是我自己的 INSERT 语句了。
    /// </remarks>
    private static async Task GiveItAnEventAsync(TestServerHost host, GameId gameId, int seatCount)
    {
        var game = await host.GameRegistry.GetOrCreateAsync(gameId, CancellationToken.None);
        var result = await game.Session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
                Actor = Actor.Host,
                IdempotencyKey = $"retirement-seed:{gameId.Value}",
            },
            CancellationToken.None);

        Assert.NotEqual(CommandResultKind.Rejected, result.Kind);
    }

    /// <summary>把一桌的建桌时刻与全部事件时刻一起改掉（活跃度取最大值，两处都要动才改得动判定）。</summary>
    private static void Backdate(string databasePath, GameId gameId, DateTimeOffset when)
    {
        Execute(databasePath, "UPDATE Games SET CreatedAt = $when WHERE GameId = $id;", when, gameId);
        Execute(databasePath, "UPDATE Events SET RecordedAt = $when WHERE GameId = $id;", when, gameId);
    }

    private static void BackdateCreatedAt(string databasePath, GameId gameId, DateTimeOffset when) =>
        Execute(databasePath, "UPDATE Games SET CreatedAt = $when WHERE GameId = $id;", when, gameId);

    /// <summary>造一批"属于不存在的桌"的行（四种残渣各一条）。</summary>
    private static void InsertOrphanRows(string databasePath, string ghostGameId)
    {
        Execute(
            databasePath,
            "INSERT INTO Events (GameId, Sequence, Type, Payload, RecordedAt) VALUES ($id, 1, 'x', 'y', $when);",
            DateTimeOffset.UtcNow,
            new GameId(ghostGameId));
        Execute(
            databasePath,
            "INSERT INTO Snapshots (GameId, Sequence, MachineJson, RecordedAt) VALUES ($id, 1, NULL, $when);",
            DateTimeOffset.UtcNow,
            new GameId(ghostGameId));
        Execute(
            databasePath,
            "INSERT INTO Receipts (GameId, IdempotencyKey, FirstSequence, LastSequence) VALUES ($id, 'k', 1, 1);",
            DateTimeOffset.UtcNow,
            new GameId(ghostGameId));
        Execute(
            databasePath,
            "INSERT INTO SeatBindings (GameId, Seat, AccountId, BoundAt) VALUES ($id, 1, 1, $when);",
            DateTimeOffset.UtcNow,
            new GameId(ghostGameId));
    }

    private static DateTimeOffset? ReadCreatedAt(string databasePath, GameId gameId)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedAt FROM Games WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId.Value);
        var stored = command.ExecuteScalar();
        return stored is null or DBNull
            ? null
            : DateTimeOffset.Parse((string)stored, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long CountRows(string databasePath, string table, string gameId)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(string databasePath, string sql, DateTimeOffset when, GameId gameId)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", gameId.Value);
        if (sql.Contains("$when", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("$when", when);
        }

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 直接开一条到库的连接（不改 <c>journal_mode</c> 之类的持久属性：这里只读写几行）。
    /// </summary>
    /// <remarks><c>Pooling=False</c>：夹具不留下句柄，收尾删临时库时不会被自己的池挡住。</remarks>
    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }
}
