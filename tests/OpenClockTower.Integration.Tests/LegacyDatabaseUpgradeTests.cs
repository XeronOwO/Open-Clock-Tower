using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Contracts;
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
        Assert.True(await storyteller.InvokeAsync<bool>("SetTableLock", credential, true));
    }

    /// <summary>
    /// 升级后的库结构与**新库**一致：老库不许留下"新库没有的列"（这次的 StorytellerTicket 就是）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意与"新库"对比而不是写死期望列：写死的清单会在加列时过期，而这条判据的意义正是
    /// "升级路径与新建路径产出同一形态"——两边一起变才算对。比的是列名 + 类型 + NOT NULL
    /// + 主键位（**不比列序**：SQLite 用列名取值，补列顺序不影响语义）。
    /// </para>
    /// <para>
    /// **默认值刻意不比**：老库的 <c>Name</c> / <c>IsLocked</c> 是上一版用
    /// <c>ADD COLUMN ... DEFAULT</c> 补的（<c>NOT NULL</c> 在 SQLite 上必须带默认值才被接受），
    /// 而新库里这两列没有默认值——这是两次"合法但写法不同"的建表留下的差异，
    /// 要抹平得重建整张表。它不影响行为：所有写入都由 EF 按列名显式给值，默认值永远轮不到。
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

        Assert.Equal(freshSchema.Keys, upgraded.Keys);
        foreach (var (table, columns) in freshSchema)
        {
            Assert.Equal(columns, upgraded[table]);
        }
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

    /// <summary>读出库里每张表的列形态（列名 → 类型 / NOT NULL / 主键位；默认值刻意不参与比对）。</summary>
    private static async Task<SortedDictionary<string, SortedDictionary<string, string>>> ReadSchemaAsync(
        string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var tables = new List<string>();
        await using (var listCommand = connection.CreateCommand())
        {
            listCommand.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await listCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var schema = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var columns = new SortedDictionary<string, string>(StringComparer.Ordinal);
            await using var command = connection.CreateCommand();
            // 表名来自本用例自己的库（sqlite_master），不是外部输入。
            command.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // 列 1 = 列名，2 = 声明类型，3 = NOT NULL，5 = 主键位（0 起）。
                columns[reader.GetString(1)] =
                    $"{reader.GetString(2)}|notnull={reader.GetInt32(3)}|pk={reader.GetInt32(5)}";
            }

            schema[table] = columns;
        }

        return schema;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _host?.Dispose();

        // 宿主停了，但本进程的 SQLite 连接池还可能握着库文件句柄（Windows 上就删不掉）。
        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { "legacy.db", "legacy.db-shm", "legacy.db-wal", "fresh.db", "fresh.db-shm", "fresh.db-wal" })
        {
            var path = Path.Combine(_contentRoot, file);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // 仍被占用时留下文件，由收尾统一清理；这里不静默吞掉"删除失败"的语义。
                }
            }
        }

        if (Directory.Exists(_contentRoot) && Directory.GetFileSystemEntries(_contentRoot).Length == 0)
        {
            Directory.Delete(_contentRoot);
        }
    }
}
