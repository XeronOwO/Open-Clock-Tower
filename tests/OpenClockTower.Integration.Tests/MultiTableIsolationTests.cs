using Microsoft.Extensions.Logging.Abstractions;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 多桌隔离（D-0024）：局注册表把"局"从进程级单例降为**按标识解析的实例**之后，
/// 两桌之间不许有任何可见的相互影响。
/// </summary>
/// <remarks>
/// <para>
/// 判据按验收规程 §4 的第三行写：不只是"该看见的人看见了"，还要
/// "**不该看见的人确实没看见**"——本类的核心断言都是反方向的（乙桌看不到甲桌的任何东西）。
/// </para>
/// <para>
/// 这里直接构造注册表 + 内存存储，**不经过宿主装配**：本票要证的是"隔离由结构保证"，
/// 而不是"某次接线碰巧对了"。宿主接线（Hub 按连接解析桌）是下一阶段的事，见票据。
/// </para>
/// </remarks>
public sealed class MultiTableIsolationTests
{
    private static readonly GameId TableA = new("table-a");
    private static readonly GameId TableB = new("table-b");

    /// <summary>建一个注册表：两桌都在册，事件流为空（空局也能被装载）。</summary>
    private static GameRegistry CreateRegistry()
    {
        var catalog = new FakeCatalog(
        [
            new GameSetup { GameId = TableA, Seats = [], CreatedByAccountId = new AccountId(1) },
            new GameSetup { GameId = TableB, Seats = [], CreatedByAccountId = new AccountId(2) },
        ]);

        return new GameRegistry(
            new EmptyGameStore(),
            catalog,
            new EmptySeatBindingStore(),
            new EmptyAccountStore(),
            new EmptyAbilityCatalog(),
            [],
            new FixedClock(),
            PacingOptions.Default,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task Registry_LoadsEveryTableInCatalog_NotJustTheDefault()
    {
        var registry = CreateRegistry();

        await registry.InitializeAsync(CancellationToken.None);

        // 两桌都在册——不是"只装默认那一个"。
        Assert.Equal(2, registry.GameIds.Count);
        Assert.True(registry.Contains(TableA));
        Assert.True(registry.Contains(TableB));
    }

    [Fact]
    public async Task Registry_SameIdYieldsSameInstance_DifferentIdsYieldDifferentInstances()
    {
        var registry = CreateRegistry();
        await registry.InitializeAsync(CancellationToken.None);

        var first = await registry.GetOrCreateAsync(TableA, CancellationToken.None);
        var again = await registry.GetOrCreateAsync(TableA, CancellationToken.None);
        var other = await registry.GetOrCreateAsync(TableB, CancellationToken.None);

        // 同一标识：同一个实例（并发取也只装载一次）。
        Assert.Same(first, again);
        // 不同标识：不同实例，各自绑定自己的标识。
        Assert.NotSame(first, other);
        Assert.Equal(TableA, first.GameId);
        Assert.Equal(TableB, other.GameId);
        // 会话与读模型都不共用。
        Assert.NotSame(first.Session, other.Session);
        Assert.NotSame(first.SeatNames, other.SeatNames);
        Assert.NotSame(first.Replay, other.Replay);
    }

    [Fact]
    public async Task Registry_ReturnsNullForUnknownTable_AndDoesNotCreateIt()
    {
        var registry = CreateRegistry();
        await registry.InitializeAsync(CancellationToken.None);

        var missing = await registry.FindAsync(new GameId("no-such-table"), CancellationToken.None);

        // 未知的桌必须被明确拒绝，而不是被"顺手"建出来——打错一个标识就多一个空桌是不可接受的。
        Assert.Null(missing);
        Assert.False(registry.Contains(new GameId("no-such-table")));
    }

    [Fact]
    public async Task SeatNames_AreIsolatedPerTable()
    {
        var registry = CreateRegistry();
        await registry.InitializeAsync(CancellationToken.None);
        var tableA = await registry.GetOrCreateAsync(TableA, CancellationToken.None);
        var tableB = await registry.GetOrCreateAsync(TableB, CancellationToken.None);

        tableA.SeatNames.Set(new SeatId(1), new AccountId(1), "甲桌的一号");

        // 正向：甲桌自己看得到。
        Assert.Equal("甲桌的一号", tableA.SeatNames.NameOf(new SeatId(1)));
        // 反方向（本票核心）：乙桌同一席位号上**什么都没有**，也看不到甲桌的任何席位名。
        Assert.Null(tableB.SeatNames.NameOf(new SeatId(1)));
        Assert.Empty(tableB.SeatNames.Snapshot());

        tableB.SeatNames.Set(new SeatId(1), new AccountId(2), "乙桌的一号");

        // 同一席位号、两桌各一份，互不覆盖（这正是"两桌互相看到对方玩家名"的根因）。
        Assert.Equal("甲桌的一号", tableA.SeatNames.NameOf(new SeatId(1)));
        Assert.Equal("乙桌的一号", tableB.SeatNames.NameOf(new SeatId(1)));
    }

    [Fact]
    public async Task RenameInOneTable_DoesNotLeakToAnother()
    {
        var registry = CreateRegistry();
        await registry.InitializeAsync(CancellationToken.None);
        var tableA = await registry.GetOrCreateAsync(TableA, CancellationToken.None);
        var tableB = await registry.GetOrCreateAsync(TableB, CancellationToken.None);

        tableA.SeatNames.Set(new SeatId(2), new AccountId(7), "老王");
        tableB.SeatNames.Set(new SeatId(2), new AccountId(7), "老王");

        tableA.SeatNames.Rename(new AccountId(7), "老王改名了");

        Assert.Equal("老王改名了", tableA.SeatNames.NameOf(new SeatId(2)));
        // 同一个账号在另一桌的名字不受影响：读模型按局隔离，改名只作用于本局。
        Assert.Equal("老王", tableB.SeatNames.NameOf(new SeatId(2)));
    }

    private sealed class FakeCatalog(IReadOnlyList<GameSetup> setups) : IGameCatalog
    {
        public Task<GameSetup?> FindAsync(GameId gameId, CancellationToken cancellationToken) =>
            Task.FromResult(setups.FirstOrDefault(setup => setup.GameId == gameId));

        public Task SaveAsync(GameSetup setup, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<GameSetup>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(setups);

        public Task UpdateLobbyAsync(
            GameId gameId,
            string name,
            bool isLocked,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyGameStore : IGameStore
    {
        public Task<IReadOnlyList<StoredEvent>> ReadEventsAsync(
            GameId gameId,
            long afterSequence,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StoredEvent>>([]);

        public Task<long> FindLastSequenceAsync(GameId gameId, CancellationToken cancellationToken) =>
            Task.FromResult(0L);

        public Task<StoredSnapshot?> FindSnapshotAsync(GameId gameId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredSnapshot?>(null);

        public Task<CommandReceipt?> FindReceiptAsync(
            GameId gameId,
            string idempotencyKey,
            CancellationToken cancellationToken) => Task.FromResult<CommandReceipt?>(null);

        public Task CommitAsync(GameCommit commit, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySeatBindingStore : ISeatBindingStore
    {
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult<SeatBinding?>(null);

        public Task<SeatBinding?> FindByAccountAsync(
            GameId gameId,
            AccountId accountId,
            CancellationToken cancellationToken) => Task.FromResult<SeatBinding?>(null);

        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(
            GameId gameId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SeatBinding>>([]);

        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class EmptyAccountStore : IAccountStore
    {
        public Task<Account?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(null);

        public Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(null);

        public Task<Account?> TryCreateAsync(NewAccount account, CancellationToken cancellationToken) =>
            Task.FromResult<Account?>(null);

        public Task<bool> TryUpdateAsync(Account account, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class EmptyAbilityCatalog : IAbilityResolutionCatalog
    {
        public IAbilityResolution? Find(CharacterId character) => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    }
}
