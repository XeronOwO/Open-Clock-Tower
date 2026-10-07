using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 旧库升级（D-0027 之后）：从"说书人票据"时代升上来的库，既要**读得出**，也要**照常开新桌**。
/// </summary>
/// <remarks>
/// <para>
/// 真机咬出来的缺陷：本版不再映射 <c>Games.StorytellerTicket</c>，而老库里那一列是
/// <c>NOT NULL</c> 且**没有默认值**——升级后老桌照旧读得出来（大厅里显示"没有房主"），
/// 但**开新桌的 INSERT 会被 SQLite 当场拒掉**（<c>NOT NULL constraint failed: Games.StorytellerTicket</c>）。
/// "老库能读不能写"在界面上只表现为"开桌失败"，所以在真宿主 + 真库上锁住它。
/// </para>
/// <para>
/// 老库结构不是凭记忆手写的近似：<see cref="LegacySchema"/> 抄自一台真机部署的 <c>sqlite_master</c>
/// ——包含上一版补列留下的书写形态（<c>Name</c> / <c>IsLocked</c> 缀在括号内）与那列退场凭据。
/// </para>
/// </remarks>
public sealed class LegacyDatabaseUpgradeTests : IDisposable
{
    /// <summary>票据时代真机库的结构（逐字抄自 <c>sqlite_master</c>，只去了无关的空白）。</summary>
    private const string LegacySchema = """
        CREATE TABLE IF NOT EXISTS "Events" (
            "GameId" TEXT NOT NULL,
            "Sequence" INTEGER NOT NULL,
            "Type" TEXT NOT NULL,
            "Payload" TEXT NOT NULL,
            "RecordedAt" TEXT NOT NULL,
            CONSTRAINT "PK_Events" PRIMARY KEY ("GameId", "Sequence")
        );
        CREATE TABLE IF NOT EXISTS "Games" (
            "GameId" TEXT NOT NULL CONSTRAINT "PK_Games" PRIMARY KEY,
            "SeatsJson" TEXT NOT NULL,
            "StorytellerTicket" TEXT NOT NULL
        , Name TEXT NOT NULL DEFAULT '', IsLocked INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS "Receipts" (
            "GameId" TEXT NOT NULL,
            "IdempotencyKey" TEXT NOT NULL,
            "FirstSequence" INTEGER NOT NULL,
            "LastSequence" INTEGER NOT NULL,
            CONSTRAINT "PK_Receipts" PRIMARY KEY ("GameId", "IdempotencyKey")
        );
        CREATE TABLE IF NOT EXISTS "SeatBindings" (
            "GameId" TEXT NOT NULL,
            "Seat" INTEGER NOT NULL,
            "AccountId" INTEGER NOT NULL,
            "BoundAt" TEXT NOT NULL,
            CONSTRAINT "PK_SeatBindings" PRIMARY KEY ("GameId", "Seat")
        );
        CREATE TABLE IF NOT EXISTS "Snapshots" (
            "GameId" TEXT NOT NULL CONSTRAINT "PK_Snapshots" PRIMARY KEY,
            "Sequence" INTEGER NOT NULL,
            "MachineJson" TEXT NULL,
            "RecordedAt" TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
            "UsernameKey" TEXT NOT NULL,
            "Username" TEXT NOT NULL,
            "DisplayName" TEXT NOT NULL,
            "PasswordHash" TEXT NOT NULL,
            "RecoveryCodeHash" TEXT NULL
        );
        CREATE UNIQUE INDEX "IX_SeatBindings_GameId_AccountId" ON "SeatBindings" ("GameId", "AccountId");
        CREATE UNIQUE INDEX "IX_Users_UsernameKey" ON "Users" ("UsernameKey");
        """;

    /// <summary>老库里那一桌：旧版自建的 <c>default</c> 桌，带着按席位发的票据与说书人票据。</summary>
    private const string LegacyDefaultTable =
        """
        INSERT INTO "Games" ("GameId", "SeatsJson", "StorytellerTicket", "Name", "IsLocked")
        VALUES ('default',
                '[{"seat":{"value":1},"ticket":"seat-1-legacy"},{"seat":{"value":2},"ticket":"seat-2-legacy"}]',
                'storyteller-legacy', '', 0);
        """;

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-legacy-{Guid.NewGuid():N}");
    private readonly string _legacyDatabasePath;
    private readonly string _freshDatabasePath;
    private readonly List<HubConnection> _connections = [];
    private WebApplicationFactory<Program>? _host;

    /// <summary>构造测试：建临时目录，并准备好老库与新库两个文件路径（老库在用例里灌结构）。</summary>
    public LegacyDatabaseUpgradeTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _legacyDatabasePath = Path.Combine(_contentRoot, "legacy.db");
        _freshDatabasePath = Path.Combine(_contentRoot, "fresh.db");
    }

    /// <summary>
    /// 核心用例：老库升上来之后，**开新桌照样成功**，而且新桌归开桌账号、他真的进得去主持台。
    /// </summary>
    /// <remarks>
    /// 升级后的老桌仍然是"没有房主"的（本版不做猜测性回填），所以这条用例同时也是
    /// "两种桌能并存"的判据：老桌读得出、新桌开得动、互不牵连。
    /// </remarks>
    [Fact]
    public async Task LegacyTicketEraDatabase_AfterUpgrade_StillCreatesNewTables()
    {
        await CreateLegacyDatabaseAsync();
        StartHost();
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "after-upgrade", "升级后的人");
        Assert.True(registered.Ok, registered.Message);

        // 老桌读得出来，且如实显示为"没有房主"（谁也进不去它的主持台）。
        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", registered.AccountSession);
        var legacy = Assert.Single(tables);
        Assert.Equal("default", legacy.GameId);
        Assert.False(legacy.CreatedByMe);
        Assert.Equal(2, legacy.SeatCapacity);

        // 这一条就是本次缺陷的判据：老库在册一桌之后，还能不能再开新桌。
        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "升级后开的桌", 5);

        Assert.True(created.Ok, $"{created.Code}：{created.Message}");

        var (storyteller, credential) = await ConnectStorytellerAsync(created.GameId, registered.AccountSession!);
        Assert.True(await storyteller.InvokeAsync<bool>("SetTableInviteOnly", credential, true));
    }

    /// <summary>
    /// 升级后的库结构与**新库**一致：老库不许留下"新库没有的列"（这次的 StorytellerTicket 就是）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意与"新库"对比而不是写死期望列：写死的清单会在加列时过期，而这条判据的意义正是
    /// "升级路径与新建路径产出同一形态"——两边一起变才算对。比的是表 / 列（名 + 类型 + NOT NULL
    /// + 主键位）/ 索引（名 + 列 + 唯一性）；**不比列序**：SQLite 用列名取值，补列顺序不影响语义。
    /// </para>
    /// <para>
    /// **默认值刻意不比**：老库的 <c>Name</c> / <c>IsLocked</c> 是上一版用
    /// <c>ADD COLUMN ... DEFAULT</c> 补的（<c>NOT NULL</c> 在 SQLite 上必须带默认值才被接受），
    /// 而新库里这两列没有默认值——这是两次"合法但写法不同"的建表留下的差异，
    /// 要抹平得重建整张表。它不影响行为：所有写入都由 EF 按列名显式给值，默认值永远轮不到。
    /// </para>
    /// <para>
    /// 结构由**产品侧那个读取器**读（<see cref="SqliteSchemaReader"/>），不再由用例自己扫
    /// <c>PRAGMA</c>：两套读法迟早会分叉，而分叉之后这条用例就不再证明产品看到的东西了。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LegacyTicketEraDatabase_AfterUpgrade_HasSameShapeAsFreshDatabase()
    {
        await CreateLegacyDatabaseAsync();
        StartHost();
        await using var account = await ConnectAccountAsync();
        _ = await RegisterAsync(account, "shape-check", "查结构的人");

        await using (var fresh = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite($"Data Source={_freshDatabasePath}")
                .Options))
        {
            await fresh.Database.EnsureCreatedAsync();
        }

        var upgraded = await ReadSchemaAsync(_legacyDatabasePath);
        var freshSchema = await ReadSchemaAsync(_freshDatabasePath);

        Assert.Equal(freshSchema.ToCanonicalText(), upgraded.ToCanonicalText());
    }

    /// <summary>
    /// 升级的**版本记账**（G-A6-2）：老库跑完 v1 之后，库文件里的版本就是本版支持的最新版，
    /// 而且结构与 EF 模型逐项一致（退场列没了、补的列在、两条唯一索引都在）。
    /// </summary>
    /// <remarks>
    /// 版本号必须真的落进库文件（而不是只活在启动日志里）：它是下次启动判断
    /// "还要不要迁移"与"这个库是不是比程序新"的唯一依据。两条唯一索引单列出来断言，
    /// 是因为它们是"一号一人 / 一席一人"的唯一执行者——审计说的"丢索引没人管"就是这里。
    /// </remarks>
    [Fact]
    public async Task LegacyTicketEraDatabase_AfterUpgrade_IsAtLatestVersionAndMatchesTheModel()
    {
        await CreateLegacyDatabaseAsync();
        StartHost();
        await using var account = await ConnectAccountAsync();
        _ = await RegisterAsync(account, "version-check", "查版本的人");

        Assert.Equal(SchemaMigrationCatalog.LatestVersion, await ReadUserVersionAsync(_legacyDatabasePath));

        var upgraded = await ReadSchemaAsync(_legacyDatabasePath);
        Assert.False(upgraded.HasColumn("Games", "StorytellerTicket"));
        Assert.True(upgraded.HasColumn("Games", "CreatedByAccountId"));
        Assert.True(upgraded.TableNamed("Users")?.IndexNamed("IX_Users_UsernameKey")?.IsUnique);
        Assert.True(upgraded.TableNamed("SeatBindings")?.IndexNamed("IX_SeatBindings_GameId_AccountId")?.IsUnique);
        Assert.Empty(SchemaComparer.Compare(ModelSchema(), upgraded));
    }

    /// <summary>
    /// 再老一步的库（**连账号表都还没有**）也能平滑升上来：缺的表由 v1 迁移补建，不再是"请换新库"。
    /// </summary>
    /// <remarks>
    /// 这条取代的是 D-0021 时代的口径——那时缺表就显式失败并提示换新库，而"换新库"对使用者
    /// 等于**丢掉全部对局数据**。现在缺表只是迁移清单里的一条 <c>CREATE TABLE IF NOT EXISTS</c>：
    /// 跑一遍就补齐，补完还要过结构核对（列 / 索引不对一样会被拦下来）。
    /// </remarks>
    [Fact]
    public async Task LegacyDatabaseWithoutAccountTables_IsHealedByTheBaselineMigration()
    {
        await CreateLegacyDatabaseAsync();
        await ExecuteSqlAsync(_legacyDatabasePath, "DROP TABLE \"Users\";");

        StartHost();
        await using var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "healed", "被治好的人");

        Assert.True(registered.Ok, registered.Message);
        var schema = await ReadSchemaAsync(_legacyDatabasePath);
        Assert.True(schema.HasTable("Users"));
        Assert.True(schema.TableNamed("Users")?.IndexNamed("IX_Users_UsernameKey")?.IsUnique);
    }

    /// <summary>
    /// v2 迁移给老库补上**建桌时刻**（M5 / G-A6-5）：开过局的按首条事件回填，从未开局的留空。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 回收要回答"这一桌空了多久"，而空桌的定义恰恰是**没有任何事件**——没有事件就没有时间戳，
    /// 于是"刚开出来五分钟"与"挂了半年"长得一模一样。这一列是唯一能区分它们的事实。
    /// </para>
    /// <para>
    /// 回填取**首条**事件而不是末条：活跃度是"建桌 / 末条事件 / 末次绑定"三者的最大值，
    /// 而首条 ≤ 末条，所以回填值不可能把任何一张桌推早到期（它只是让报表有个数）。
    /// 从未开局的老桌回填后仍是空 —— **没有依据就不删**，这是有意的。
    /// </para>
    /// <para>
    /// 不启宿主直接跑迁移：这里判的是**迁移本身**（列补上没有、回填值对不对），
    /// 而老库那两条 <c>Type='t'</c> 的假事件会让宿主在恢复时走降级路径——那噪音不属于这条判据。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LegacyDatabase_V2Migration_BackfillsCreationTimeFromTheFirstEvent()
    {
        await CreateLegacyDatabaseAsync();
        await ExecuteSqlAsync(
            _legacyDatabasePath,
            """
            INSERT INTO "Games" ("GameId", "SeatsJson", "StorytellerTicket", "Name", "IsLocked")
            VALUES ('never-played', '[]', 'storyteller-legacy', '', 0);
            """);
        await ExecuteSqlAsync(
            _legacyDatabasePath,
            """
            INSERT INTO "Events" ("GameId", "Sequence", "Type", "Payload", "RecordedAt") VALUES
                ('default', 1, 't', '{}', '2026-01-01 00:00:00+00:00'),
                ('default', 2, 't', '{}', '2026-03-01 00:00:00+00:00');
            """);

        await using var db = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite($"Data Source={_legacyDatabasePath}")
                .Options);
        var version = await DatabaseSchemaUpgrader.UpgradeAsync(
            db,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(SchemaMigrationCatalog.LatestVersion, version);
        Assert.Equal("2026-01-01 00:00:00+00:00", await ReadCreatedAtAsync(_legacyDatabasePath, "default"));
        Assert.Null(await ReadCreatedAtAsync(_legacyDatabasePath, "never-played"));
    }

    /// <summary>
    /// v3 迁移把老库的**访问模式列正名**（`IsLocked` → `IsInviteOnly`，D-0037）：改名、值不变、可逆。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一列的**效果**从来没变过（自助入座被拒、持票据者照进），变的是名字与口径：
    /// 当年叫"锁桌"，审计因此把"锁桌没拦住票据入座"记成缺陷（G-A4-2）；按新的访问模型，
    /// 那正是**邀请制**的语义。正名之外这一条还要证明两件事：**值一个字节都没动**
    /// （邀请制的那一桌升级后仍然是邀请制），以及**退场列没有被顺手带回来**。
    /// </para>
    /// <para>
    /// 不启宿主直接跑迁移：这里判的是迁移本身，而老库那两条 <c>Type='t'</c> 的假事件
    /// 会让宿主在恢复时走降级路径——那噪音不属于这条判据（与 v2 那条同款）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LegacyDatabase_V3Migration_RenamesAccessModeColumnAndKeepsItsValue()
    {
        await CreateLegacyDatabaseAsync();
        // 老库里有一桌是"锁着"的（旧口径下的邀请制），它的值必须原样跟到新列名上。
        await ExecuteSqlAsync(
            _legacyDatabasePath,
            """
            UPDATE "Games" SET "IsLocked" = 1 WHERE "GameId" = 'default';
            """);

        await using var db = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite($"Data Source={_legacyDatabasePath}")
                .Options);
        var version = await DatabaseSchemaUpgrader.UpgradeAsync(
            db,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(SchemaMigrationCatalog.LatestVersion, version);

        var upgraded = await ReadSchemaAsync(_legacyDatabasePath);
        Assert.True(upgraded.HasColumn("Games", SchemaMigrationCatalog.InviteOnlyColumn));
        Assert.False(upgraded.HasColumn("Games", SchemaMigrationCatalog.LegacyLockedColumn));
        Assert.Equal(1, await ReadInviteOnlyAsync(_legacyDatabasePath, "default"));
        Assert.Empty(SchemaComparer.Compare(ModelSchema(), upgraded));
    }

    /// <summary>
    /// v4 迁移把老库里的**明文席位票据抹掉**（D-0038 / 审计 G-A2-2）：席位列只剩席位号，
    /// 旧的那一串码进不来，而说书人新签的一枚照进——升级不该把老桌变成进不去的孤岛。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 老形态是 <c>[{"seat":{"value":1},"ticket":"seat-1-legacy"}, …]</c>：**凭据就住在席位名单里**。
    /// 本版把凭据搬进自己的表（只存哈希），于是那一批明文必须当场作废——抹掉的是明文本身，
    /// 这也是这条迁移**不可逆**的原因（旧程序拿回旧包也读不了这个库）。
    /// </para>
    /// <para>
    /// 判据分三半，缺一条都不算收口：**库里读不到明文**（席位列的读数）、**旧码进不来**
    /// （真链路入座被拒）、**新码进得来**（没有为了安全把功能弄坏）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LegacyTicketEraDatabase_PlaintextTicketsAreErased_AndOldCodesAreRejected()
    {
        await CreateLegacyDatabaseAsync();
        StartHost();
        await using var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "legacy-code", "旧码探针");
        Assert.True(registered.Ok, registered.Message);

        // ① 库里只剩席位号：两位席位都在（名单没丢），而 ticket 字段与它携带的明文一起没了。
        Assert.Equal("[1,2]", await ReadSeatsJsonAsync(_legacyDatabasePath, "default"));
        Assert.Equal(SchemaMigrationCatalog.LatestVersion, await ReadUserVersionAsync(_legacyDatabasePath));
        Assert.True((await ReadSchemaAsync(_legacyDatabasePath)).HasTable(SchemaMigrationCatalog.SeatInvitationTable));

        // ② 旧码进不来：服务端没有它的哈希，凭据表里根本没有这一条。
        await using var game = await ConnectGameAsync("default");
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => game.InvokeAsync<SeatJoinDto>("JoinByInviteCode", "seat-1-legacy", registered.AccountSession, 0L));
        Assert.Contains("邀请码无效", rejected.Message, StringComparison.Ordinal);

        // ③ 新签的一枚照进：老桌照常能进人（升级没有把它的入口一起关掉）。
        var issued = await _host!.Services
            .GetRequiredService<SeatInvitationService>()
            .IssueAsync(new GameId("default"), new SeatId(1), CancellationToken.None);
        var joined = await game.InvokeAsync<SeatJoinDto>(
            "JoinByInviteCode",
            issued.Code,
            registered.AccountSession,
            0L);
        Assert.Equal(1, joined.Bundle.View.Seat);
    }

    /// <summary>读某一桌的席位列（库里那一列的**原文**：判"明文没了"必须看它，而不是看 EF 读回来的对象）。</summary>
    private static async Task<string?> ReadSeatsJsonAsync(string databasePath, string gameId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SeatsJson FROM Games WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>连一条**玩家侧**的游戏连接（不加入，只是拿它试入座）。</summary>
    private async Task<HubConnection> ConnectGameAsync(string gameId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_host!.Server.BaseAddress, $"/hub/game?gameId={Uri.EscapeDataString(gameId)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>读某一桌的访问模式列（正名之后的名字）。</summary>
    private static async Task<long?> ReadInviteOnlyAsync(string databasePath, string gameId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{SchemaMigrationCatalog.InviteOnlyColumn}\" FROM \"Games\" WHERE \"GameId\" = $gameId;";
        command.Parameters.AddWithValue("$gameId", gameId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    /// <summary>把真机老库的结构与那一桌灌进临时库（用例自己造老库，不依赖任何外部数据）。</summary>
    private async Task CreateLegacyDatabaseAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_legacyDatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"{LegacySchema}\n{LegacyDefaultTable}";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>起真宿主（真 SignalR / 真 SQLite），库指向刚灌好的老库。</summary>
    private void StartHost()
    {
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", _legacyDatabasePath);
            builder.UseSetting("GameServer:SeatCount", "5");
        });
    }

    private async Task<HubConnection> ConnectAccountAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_host!.Server.BaseAddress, "/hub/account"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    private async Task<AccountDto> RegisterAsync(HubConnection connection, string username, string displayName) =>
        await connection.InvokeAsync<AccountDto>("Register", username, displayName, "password-123");

    private async Task<(HubConnection Connection, string Credential)> ConnectStorytellerAsync(
        string gameId,
        string accountSession)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_host!.Server.BaseAddress, $"/hub/game?gameId={Uri.EscapeDataString(gameId)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>(
            "JoinStorytellerWithAccount",
            accountSession);
        _connections.Add(connection);
        return (connection, joined.Credential);
    }

    /// <summary>读出库的结构（**产品侧那个读取器**：用例与运行时看到的是同一份形状）。</summary>
    private static async Task<DatabaseSchema> ReadSchemaAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        return await SqliteSchemaReader.ReadAsync(connection, CancellationToken.None);
    }

    /// <summary>库文件里的结构版本（<c>user_version</c>）。</summary>
    private static async Task<int> ReadUserVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        return await SqliteUserVersion.ReadAsync(connection, CancellationToken.None);
    }

    /// <summary>某一桌的建桌时刻（<c>null</c> = 老库里的空桌：空闲多久无法判定）。</summary>
    private static async Task<string?> ReadCreatedAtAsync(string databasePath, string gameId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedAt FROM Games WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>对库跑一条 SQL（用例用它把老库改成某个具体形态，例如丢掉账号表）。</summary>
    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>EF 模型说的"库应该长什么样"（不给它任何连接，只借模型）。</summary>
    private static DatabaseSchema ModelSchema()
    {
        using var db = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>().UseSqlite("Data Source=:memory:").Options);
        return SchemaContract.FromModel(db);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _host?.Dispose();

        TestDatabaseFiles.Delete(Path.Combine(_contentRoot, "legacy.db"));
        TestDatabaseFiles.Delete(Path.Combine(_contentRoot, "fresh.db"));

        if (Directory.Exists(_contentRoot) && Directory.GetFileSystemEntries(_contentRoot).Length == 0)
        {
            Directory.Delete(_contentRoot);
        }
    }
}
