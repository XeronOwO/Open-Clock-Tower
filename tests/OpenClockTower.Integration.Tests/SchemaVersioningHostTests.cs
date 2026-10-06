using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 结构版本与迁移（M5 / G-A6-1 · G-A6-2 · G-A6-3）：真宿主 + 真 SQLite 文件上的行为。
/// </summary>
/// <remarks>
/// <para>
/// 这几条判据都**必须**落在磁盘上那个库上：审计要的验收原话是
/// "手工删掉一条唯一索引后启动，守卫能报出来并自愈"，以及"同机起第二个实例时得到明确错误
/// 而不是'请换新库'"。看代码是看不出来的——所以每条都真起一次宿主、真读一次库。
/// </para>
/// <para>
/// 每处改坏都用**外部 SQL**做（<c>DROP INDEX</c> / <c>DROP COLUMN</c> / <c>PRAGMA user_version</c>），
/// 而不去改产品代码：改坏产品代码证明的是"用例会红"，改坏**库**证明的才是"守卫真的在看着库"。
/// </para>
/// </remarks>
public sealed class SchemaVersioningHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oct-schema-{Guid.NewGuid():N}");

    /// <summary>构造用例：建一个临时根目录，每个场景一个库文件。</summary>
    public SchemaVersioningHostTests() => Directory.CreateDirectory(_root);

    /// <summary>全新库：由 v1 迁移建出来，版本写进库文件，形状与 EF 模型逐项一致。</summary>
    [Fact]
    public async Task FreshDatabase_IsBuiltByMigrationAndMatchesTheModel()
    {
        var path = Path.Combine(_root, "fresh.db");

        await using (var host = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(path));
            // 迁移真的跑过要看得见：日志里有那条"应用迁移 v1"，否则"库是 EnsureCreated 建的吗"没法回答。
            Assert.Contains(
                host.Logs,
                line => line.Contains("应用迁移 v1", StringComparison.Ordinal));
            Assert.Contains(
                host.Logs,
                line => line.Contains($"结构版本={SchemaMigrationCatalog.LatestVersion}", StringComparison.Ordinal));
        }

        Assert.Equal(SchemaMigrationCatalog.LatestVersion, await ReadUserVersionAsync(path));
        Assert.Empty(SchemaComparer.Compare(ModelSchema(path), await ReadLiveSchemaAsync(path)));
    }

    /// <summary>
    /// 迁移建出来的库与 **EF 模型建出来的库**同形：两条路径产出同一份结构，逐字段比对。
    /// </summary>
    /// <remarks>
    /// 这是"手写 DDL 会不会与实体漂移"的判据。漂移了会在两处红：这条用例，以及每次启动的结构核对。
    /// 拿 <c>EnsureCreated</c> 当对照是刻意的——它是模型最直接的产物，而迁移路径正是要取代它的那一条。
    /// </remarks>
    [Fact]
    public async Task MigratedDatabase_HasTheSameShapeAsTheModelBuilds()
    {
        var migrated = Path.Combine(_root, "migrated.db");
        var fromModel = Path.Combine(_root, "from-model.db");

        await using (var host = new TestServerHost(databasePath: migrated, deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(migrated));
            Assert.Contains(TestServerHost.GameId, host.GameRegistry.GameIds);
        }

        await using (var db = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>().UseSqlite($"Data Source={fromModel}").Options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        Assert.Equal(
            (await ReadLiveSchemaAsync(fromModel)).ToCanonicalText(),
            (await ReadLiveSchemaAsync(migrated)).ToCanonicalText());
    }

    /// <summary>
    /// 库比程序新 ⇒ **拒绝启动**（G-A6-3 的"不支持降级"那一半）。
    /// </summary>
    /// <remarks>
    /// 现场是"程序回滚了、库没回"：迁移只进不退，旧程序看不懂新结构，
    /// 这时继续跑只会把数据写坏。要求是**明确报错**，而不是"尽量试试"。
    /// </remarks>
    [Fact]
    public async Task DatabaseFromANewerProgram_RefusesToStart()
    {
        var path = Path.Combine(_root, "from-the-future.db");
        await using (var seeding = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(path));
        }

        await ExecuteSqlAsync(path, $"PRAGMA user_version = {SchemaMigrationCatalog.LatestVersion + 1};");

        var exception = Record.Exception(() => new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false));

        Assert.NotNull(exception);
        AssertMessageContains(exception, "比本程序支持的");
        // 版本号要出现在信息里：运维第一眼要能判断"是程序旧了还是库新了"。
        AssertMessageContains(exception, (SchemaMigrationCatalog.LatestVersion + 1).ToString());
    }

    /// <summary>
    /// 同一份库起第二个实例 ⇒ **明确报"另一个实例在跑"**，而不是伪装成"库坏了，请换新库"。
    /// </summary>
    /// <remarks>
    /// 审计的原话是"误开两个实例会把启动错误伪装成库坏了"。判据因此有两条：
    /// 消息里必须点明"另一个实例"，而且**不许**出现"换新库"这种会让人去删库的建议。
    /// </remarks>
    [Fact]
    public async Task SecondInstanceOnTheSameDatabase_IsRefusedWithAClearMessage()
    {
        var path = Path.Combine(_root, "shared.db");

        await using var first = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false);
        var exception = Record.Exception(() => new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false));

        Assert.NotNull(exception);
        AssertMessageContains(exception, "另一个实例");
        Assert.DoesNotContain("换新库", exception.ToString(), StringComparison.Ordinal);
        // 第一个实例照常服务（第二个进程失败不该影响正在跑的那一个）。
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// 手工删掉一条唯一索引 ⇒ 启动时**报出来并自愈**，而且自愈后的索引真的在拦重复。
    /// </summary>
    /// <remarks>
    /// 这是审计给的验收判据原文（G-A6-1）。判据刻意分两层：
    /// ① 索引回来了（元数据）；② 插两行重复值会被拒（**行为**）——只判①的话，
    /// 建出一条非唯一的同名索引也会"通过"。
    /// </remarks>
    [Fact]
    public async Task MissingUniqueIndex_IsRebuiltAndEnforcesUniquenessAgain()
    {
        var path = Path.Combine(_root, "heal-index.db");
        await using (var seeding = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(path));
        }

        await ExecuteSqlAsync(path, "DROP INDEX \"IX_Users_UsernameKey\";");
        Assert.Null((await ReadLiveSchemaAsync(path)).TableNamed("Users")?.IndexNamed("IX_Users_UsernameKey"));

        await using (var healed = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false))
        {
            Assert.Contains(
                healed.Logs,
                line => line.Contains("结构自愈", StringComparison.Ordinal)
                        && line.Contains("IX_Users_UsernameKey", StringComparison.Ordinal));
        }

        var index = (await ReadLiveSchemaAsync(path)).TableNamed("Users")?.IndexNamed("IX_Users_UsernameKey");
        Assert.NotNull(index);
        Assert.True(index.IsUnique);

        // 行为面：自愈之后"一号一人"这条规则重新由数据库执行。
        await ExecuteSqlAsync(path, InsertUser("dup", "第一个"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteSqlAsync(path, InsertUser("dup", "第二个")));
    }

    /// <summary>
    /// 库里缺一列（迁移没跟上 / 有人手工改过）⇒ **拒绝启动**，并把差在哪写清楚。
    /// </summary>
    /// <remarks>
    /// 拒绝而不是自愈，是因为"缺列"最常见的成因是**改名**：自愈会补一个空列，
    /// 旧列的数据原封不动地留在那儿，看起来一切正常，实际已经丢了。
    /// </remarks>
    [Fact]
    public async Task MissingColumn_RefusesToStartWithTheExactDifference()
    {
        var path = Path.Combine(_root, "missing-column.db");
        await using (var seeding = new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false))
        {
            Assert.True(File.Exists(path));
        }

        // 版本号保持"最新"：于是启动时没有迁移可跑，能红的只可能是结构核对。
        await ExecuteSqlAsync(path, "ALTER TABLE Games DROP COLUMN Name;");

        var exception = Record.Exception(() => new TestServerHost(databasePath: path, deleteDatabaseOnDispose: false));

        Assert.NotNull(exception);
        AssertMessageContains(exception, "库结构与本版模型不一致");
        AssertMessageContains(exception, "Games.Name");
    }

    /// <summary>
    /// 迁移清单自检：版本从 1 起连续、不重复、每条都有说明，且"欠哪些"算得对。
    /// </summary>
    /// <remarks>
    /// 这不是形式主义：版本号断号或重复会让"库升到哪一版"变成一句无法回答的话，
    /// 而唯一能防住它的地方就是这里——清单是纯数据，断言也是。
    /// </remarks>
    [Fact]
    public void MigrationCatalog_IsContiguousDescribedAndQueryable()
    {
        var migrations = SchemaMigrationCatalog.All;

        Assert.NotEmpty(migrations);
        Assert.Equal(Enumerable.Range(1, migrations.Count), migrations.Select(migration => migration.Version));
        Assert.All(migrations, migration => Assert.False(string.IsNullOrWhiteSpace(migration.Description)));
        Assert.Equal(migrations.Count, migrations.Select(migration => migration.Version).Distinct().Count());
        Assert.Equal(migrations[^1].Version, SchemaMigrationCatalog.LatestVersion);
        Assert.Empty(SchemaMigrationCatalog.Pending(SchemaMigrationCatalog.LatestVersion));
        Assert.Equal(migrations.Count, SchemaMigrationCatalog.Pending(0).Count);
        Assert.Equal(
            migrations.Skip(1).Select(migration => migration.Version),
            SchemaMigrationCatalog.Pending(1).Select(migration => migration.Version));
    }

    /// <summary>插入一行账号用的 SQL（自愈之后要能证明唯一索引真的在拦重复）。</summary>
    private static string InsertUser(string usernameKey, string displayName) =>
        "INSERT INTO Users (UsernameKey, Username, DisplayName, PasswordHash, RecoveryCodeHash) VALUES "
        + $"('{usernameKey}', '{usernameKey}', '{displayName}', 'hash', NULL);";

    private static async Task<int> ReadUserVersionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        return await SqliteUserVersion.ReadAsync(connection, CancellationToken.None);
    }

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DatabaseSchema> ReadLiveSchemaAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        return await SqliteSchemaReader.ReadAsync(connection, CancellationToken.None);
    }

    /// <summary>EF 模型说的"这个库应该长什么样"（不给它任何连接，只借模型）。</summary>
    private static DatabaseSchema ModelSchema(string databasePath)
    {
        using var db = new GameDbContext(
            new DbContextOptionsBuilder<GameDbContext>().UseSqlite($"Data Source={databasePath}").Options);
        return SchemaContract.FromModel(db);
    }

    /// <summary>
    /// 断言异常链里出现过某段文字。
    /// </summary>
    /// <remarks>
    /// 走整条链是必须的：宿主起不来时框架会把自己那层包在外面（<c>WebApplicationFactory</c> /
    /// <c>HostFactoryResolver</c> 都会包），只看最外层等于只断言了"抛过异常"。
    /// </remarks>
    private static void AssertMessageContains(Exception exception, string marker)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(marker, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"异常链里没有出现「{marker}」：{exception}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        // 逐层清：先删库与伴随文件（-wal / -shm / .lock），再删空目录（不做递归删除）。
        foreach (var file in Directory.GetFiles(_root))
        {
            TestDatabaseFiles.Delete(file);
        }

        if (Directory.Exists(_root) && Directory.GetFileSystemEntries(_root).Length == 0)
        {
            Directory.Delete(_root);
        }
    }
}
