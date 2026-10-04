using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号表与席位绑定表（D-0021）：真 SQLite 往返、唯一索引语义、更新与解除。
/// </summary>
/// <remarks>每个用例一个临时库文件，收尾时只删自己建的那一个文件（不做递归删除）。</remarks>
public sealed class AccountStoreTests : IDisposable
{
    private static readonly GameId Game = new("store-test-game");

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"oct-account-test-{Guid.NewGuid():N}.db");

    private readonly TestDbContextFactory _factory;

    /// <summary>构造测试：建库并建表。</summary>
    public AccountStoreTests()
    {
        _factory = new TestDbContextFactory(_databasePath);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // 释放连接池后再删单文件临时库；失败不静默（测试收尾也要显式）。
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    /// <summary>账号创建 / 大小写不敏感查找 / 重复拒绝 / 整份更新。</summary>
    [Fact]
    public async Task AccountStore_RoundTrips_AndEnforcesCaseInsensitiveUsername()
    {
        var store = new EfAccountStore(_factory);

        var created = await store.TryCreateAsync(
            new NewAccount
            {
                Username = "Alice",
                DisplayName = "爱丽丝",
                PasswordHash = "hash-1",
                RecoveryCodeHash = "recovery-1",
            },
            CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(1, created!.Id.Value);
        Assert.Equal("Alice", created.Username);

        var found = await store.FindByUsernameAsync("ALICE", CancellationToken.None);
        Assert.Equal(created.Id, found!.Id);
        Assert.Equal("爱丽丝", found.DisplayName);

        var duplicate = await store.TryCreateAsync(
            new NewAccount
            {
                Username = "alice",
                DisplayName = "另一个",
                PasswordHash = "hash-2",
                RecoveryCodeHash = "recovery-2",
            },
            CancellationToken.None);
        Assert.Null(duplicate);

        var updated = created with { DisplayName = "爱丽丝 2", PasswordHash = "hash-3", RecoveryCodeHash = "recovery-3" };
        Assert.True(await store.TryUpdateAsync(updated, CancellationToken.None));
        var reloaded = await store.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.Equal("爱丽丝 2", reloaded!.DisplayName);
        Assert.Equal("hash-3", reloaded.PasswordHash);

        Assert.False(await store.TryUpdateAsync(
            updated with { Id = new AccountId(999) },
            CancellationToken.None));
    }

    /// <summary>绑定表的两个唯一约束：一席一账号、一账号一席；解除后可以重新绑定。</summary>
    [Fact]
    public async Task BindingStore_EnforcesSeatAndAccountUniqueness()
    {
        var store = new EfSeatBindingStore(_factory);
        var now = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.FromHours(8));

        Assert.True(await store.TryBindAsync(
            new SeatBinding { GameId = Game, Seat = new SeatId(1), AccountId = new AccountId(1), BoundAt = now },
            CancellationToken.None));

        // 同一席位换账号 / 同一账号换席位都写不进去。
        Assert.False(await store.TryBindAsync(
            new SeatBinding { GameId = Game, Seat = new SeatId(1), AccountId = new AccountId(2), BoundAt = now },
            CancellationToken.None));
        Assert.False(await store.TryBindAsync(
            new SeatBinding { GameId = Game, Seat = new SeatId(2), AccountId = new AccountId(1), BoundAt = now },
            CancellationToken.None));

        Assert.True(await store.TryBindAsync(
            new SeatBinding { GameId = Game, Seat = new SeatId(2), AccountId = new AccountId(2), BoundAt = now },
            CancellationToken.None));

        var bySeat = await store.FindBySeatAsync(Game, new SeatId(2), CancellationToken.None);
        Assert.Equal(new AccountId(2), bySeat!.AccountId);
        var byAccount = await store.FindByAccountAsync(Game, new AccountId(1), CancellationToken.None);
        Assert.Equal(new SeatId(1), byAccount!.Seat);

        var all = await store.ListByGameAsync(Game, CancellationToken.None);
        Assert.Equal([new SeatId(1), new SeatId(2)], all.Select(binding => binding.Seat));

        Assert.True(await store.TryReleaseAsync(Game, new SeatId(1), CancellationToken.None));
        Assert.False(await store.TryReleaseAsync(Game, new SeatId(1), CancellationToken.None));
        Assert.True(await store.TryBindAsync(
            new SeatBinding { GameId = Game, Seat = new SeatId(1), AccountId = new AccountId(1), BoundAt = now },
            CancellationToken.None));
    }

    /// <summary>测试用的 DbContext 工厂：每个上下文直连同一个临时 SQLite 文件。</summary>
    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<GameDbContext>
    {
        /// <inheritdoc />
        public GameDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options);
    }
}
