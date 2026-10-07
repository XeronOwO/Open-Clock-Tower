using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 席位认领（D-0021）：一席一账号、一账号一席、重复认领幂等、并发冲突显式收敛、解除后可重认。
/// </summary>
public sealed class SeatBindingTests
{
    private static readonly GameId Game = new("test-game");

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.FromHours(8));

    /// <summary>首次认领成功并记录时刻；同一账号同一席重复认领幂等返回原绑定。</summary>
    [Fact]
    public async Task Claim_BindsSeat_AndIsIdempotentForSameAccount()
    {
        var store = new FakeBindingStore();
        var service = new SeatBindingService(store, new FixedClock(Now));

        var first = await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.Equal("ok", first.Code);
        Assert.True(first.Created);
        Assert.Equal(new SeatId(1), first.Binding!.Seat);
        Assert.Equal(Now, first.Binding.BoundAt);

        var again = await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);
        Assert.True(again.Accepted);
        Assert.False(again.Created);
        Assert.Equal(first.Binding, again.Binding);
        Assert.Single(store.Bindings);
    }

    /// <summary>席位已被别的账号认领 → 明确拒绝，不覆盖绑定。</summary>
    [Fact]
    public async Task Claim_RejectsSeatTakenByAnotherAccount()
    {
        var store = new FakeBindingStore();
        var service = new SeatBindingService(store, new FixedClock(Now));
        await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);

        var taken = await service.ClaimAsync(Game, new SeatId(1), new AccountId(2), CancellationToken.None);

        Assert.False(taken.Accepted);
        Assert.Equal("seat_taken", taken.Code);
        Assert.Equal(new AccountId(1), store.Bindings.Single().AccountId);
    }

    /// <summary>一个账号在本局只能占一席：去认领第二席被拒。</summary>
    [Fact]
    public async Task Claim_RejectsSecondSeatForSameAccount()
    {
        var store = new FakeBindingStore();
        var service = new SeatBindingService(store, new FixedClock(Now));
        await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);

        var second = await service.ClaimAsync(Game, new SeatId(2), new AccountId(1), CancellationToken.None);

        Assert.False(second.Accepted);
        Assert.Equal("account_already_seated", second.Code);
        Assert.Single(store.Bindings);
    }

    /// <summary>认领后可按账号解出席位（"只凭账号重连"路径）；解除后解不出、席位可被他人重新认领。</summary>
    [Fact]
    public async Task ResolveAndRelease_FollowTheBinding()
    {
        var store = new FakeBindingStore();
        var service = new SeatBindingService(store, new FixedClock(Now));
        await service.ClaimAsync(Game, new SeatId(3), new AccountId(1), CancellationToken.None);

        var resolved = await service.ResolveSeatAsync(Game, new AccountId(1), CancellationToken.None);
        Assert.Equal(new SeatId(3), resolved!.Seat);

        Assert.True(await service.ReleaseAsync(Game, new SeatId(3), CancellationToken.None));
        Assert.Null(await service.ResolveSeatAsync(Game, new AccountId(1), CancellationToken.None));
        Assert.False(await service.ReleaseAsync(Game, new SeatId(3), CancellationToken.None));

        var reclaimed = await service.ClaimAsync(Game, new SeatId(3), new AccountId(2), CancellationToken.None);
        Assert.True(reclaimed.Accepted);
    }

    /// <summary>并发竞态：存储挡下后重新读一次，确认席位真的被别的账号抢走 → 拒绝而不是静默通过。</summary>
    [Fact]
    public async Task Claim_OnConcurrentConflict_ReReadsAndRejects()
    {
        var store = new RacyBindingStore(new SeatBinding
        {
            GameId = Game,
            Seat = new SeatId(5),
            AccountId = new AccountId(9),
            BoundAt = Now,
        });
        var service = new SeatBindingService(store, new FixedClock(Now));

        var outcome = await service.ClaimAsync(Game, new SeatId(5), new AccountId(1), CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Equal("seat_taken", outcome.Code);
        Assert.False(outcome.Created);
    }

    /// <summary>
    /// 并发读倾斜：同账号同席的两条加入同时进来时，席位那次读发生在对方写入**之前**（看不到），
    /// 账号那次读发生在**之后**（看得到自己刚才那一次绑定）。两次读合起来指向的其实是
    /// **自己正要认领的这一席**——那就是幂等那一路，不是冲突，不许拒绝。
    /// </summary>
    /// <remarks>
    /// 现场是集成套件在集合并行下的随机红（票 <c>todo/integration-suite-parallel-flakes.md</c>）：
    /// 四条同账号同席的并发加入里有一条会吃 <c>account_already_seated</c> 而被 Hub 抛回客户端。
    /// </remarks>
    [Fact]
    public async Task Claim_OnTornReadOfOwnSeat_IsIdempotentNotRejected()
    {
        var landed = new SeatBinding
        {
            GameId = Game,
            Seat = new SeatId(1),
            AccountId = new AccountId(1),
            BoundAt = Now,
        };
        var store = new TornSeatReadBindingStore(landed);
        var service = new SeatBindingService(store, new FixedClock(Now));

        var outcome = await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);

        Assert.True(
            outcome.Accepted,
            $"同账号同席的重复认领必须幂等接受（并发下这条路径才是常态），实际被拒：{outcome.Code} / {outcome.Message}");
        Assert.False(outcome.Created);
        Assert.Equal(new SeatId(1), outcome.Binding!.Seat);
        Assert.Equal(new AccountId(1), outcome.Binding.AccountId);
    }

    /// <summary>
    /// 过期读的另一面：账号那次读看到的"我已经占的席位"可能**刚被解除**
    /// （说书人移人 / 账号注销）。只有那条绑定**现在仍然在**，才允许按"占着别的一席"拒绝。
    /// </summary>
    [Fact]
    public async Task Claim_OnStaleAccountRead_OfAReleasedSeat_IsNotRejected()
    {
        var service = new SeatBindingService(new StaleAccountReadBindingStore(), new FixedClock(Now));

        var outcome = await service.ClaimAsync(Game, new SeatId(2), new AccountId(1), CancellationToken.None);

        Assert.True(
            outcome.Accepted,
            $"那一席已经被解除，不该再挡新认领，实际被拒：{outcome.Code} / {outcome.Message}");
        Assert.True(outcome.Created);
        Assert.Equal(new SeatId(2), outcome.Binding!.Seat);
    }

    /// <summary>
    /// 撞上唯一索引、但复核那一刻占用**已经退场**（并发解除 / 注销）⇒ 收敛成**可重试**的
    /// <c>conflict</c>，而不是把一个未预期异常抛给 Hub。
    /// </summary>
    [Fact]
    public async Task Claim_WhenOccupancyVanishesBeforeTheReRead_ConvergesToConflict()
    {
        var service = new SeatBindingService(new VanishingOccupancyBindingStore(), new FixedClock(Now));

        var outcome = await service.ClaimAsync(Game, new SeatId(3), new AccountId(1), CancellationToken.None);

        Assert.False(outcome.Accepted);
        Assert.Equal("conflict", outcome.Code);
        Assert.False(outcome.Created);
    }

    /// <summary>绑定按对局隔离：同一账号在下一局要重新认领，旧局绑定不影响新局（D-0021 跨局口径）。</summary>
    [Fact]
    public async Task Claim_IsScopedPerGame()
    {
        var store = new FakeBindingStore();
        var service = new SeatBindingService(store, new FixedClock(Now));
        var nextGame = new GameId("next-game");
        await service.ClaimAsync(Game, new SeatId(1), new AccountId(1), CancellationToken.None);

        var next = await service.ClaimAsync(nextGame, new SeatId(1), new AccountId(1), CancellationToken.None);

        Assert.True(next.Accepted);
        Assert.True(next.Created);
        Assert.Equal(nextGame, next.Binding!.GameId);
        Assert.Equal(2, store.Bindings.Count);
        Assert.Equal(new SeatId(1), (await service.ResolveSeatAsync(Game, new AccountId(1), CancellationToken.None))!.Seat);
        Assert.Equal(new SeatId(1), (await service.ResolveSeatAsync(nextGame, new AccountId(1), CancellationToken.None))!.Seat);
    }

    /// <summary>内存绑定表：语义（席位唯一 + 账号唯一）与真实存储的唯一索引一致。</summary>
    private sealed class FakeBindingStore : ISeatBindingStore
    {
        private readonly Dictionary<(GameId, SeatId), SeatBinding> _bySeat = [];

        /// <summary>当前绑定集合（测试断言用）。</summary>
        public IReadOnlyCollection<SeatBinding> Bindings => _bySeat.Values;

        /// <inheritdoc />
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult(_bySeat.GetValueOrDefault((gameId, seat)));

        /// <inheritdoc />
        public Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult(Bindings.FirstOrDefault(binding =>
                binding.GameId == gameId && binding.AccountId == accountId));

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeatBinding>>(
                [.. Bindings.Where(binding => binding.GameId == gameId).OrderBy(binding => binding.Seat.Value)]);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
            IReadOnlyCollection<GameId> gameIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeatBinding>>(
                [.. Bindings.Where(binding => gameIds.Contains(binding.GameId))
                    .OrderBy(binding => binding.GameId.Value)
                    .ThenBy(binding => binding.Seat.Value)]);

        /// <inheritdoc />
        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken)
        {
            if (_bySeat.ContainsKey((binding.GameId, binding.Seat))
                || Bindings.Any(existing => existing.GameId == binding.GameId && existing.AccountId == binding.AccountId))
            {
                return Task.FromResult(false);
            }

            _bySeat[(binding.GameId, binding.Seat)] = binding;
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult(_bySeat.Remove((gameId, seat)));
    }

    /// <summary>竞态假存储：第一次写入时先塞进"竞争对手"的绑定，模拟并发抢占。</summary>
    private sealed class RacyBindingStore : ISeatBindingStore
    {
        private readonly FakeBindingStore _inner = new();
        private readonly SeatBinding _competitor;
        private bool _raced;

        public RacyBindingStore(SeatBinding competitor) => _competitor = competitor;

        /// <inheritdoc />
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            _inner.FindBySeatAsync(gameId, seat, cancellationToken);

        /// <inheritdoc />
        public Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken) =>
            _inner.FindByAccountAsync(gameId, accountId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken) =>
            _inner.ListByGameAsync(gameId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
            IReadOnlyCollection<GameId> gameIds,
            CancellationToken cancellationToken) => _inner.ListByGamesAsync(gameIds, cancellationToken);

        /// <inheritdoc />
        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken)
        {
            if (!_raced)
            {
                _raced = true;
                // 假存储的方法是同步完成体：调用即已写入，无需等待。
                _ = _inner.TryBindAsync(_competitor, cancellationToken);
            }

            return _inner.TryBindAsync(binding, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            _inner.TryReleaseAsync(gameId, seat, cancellationToken);
    }

    /// <summary>
    /// 读倾斜假存储：席位那次读是**旧快照**（那一刻这一席还没人），账号那次读是新快照
    /// （看得到刚落地的那条绑定）。真实并发里这两次读之间夹着的就是对方那次写入。
    /// </summary>
    private sealed class TornSeatReadBindingStore : ISeatBindingStore
    {
        private readonly FakeBindingStore _inner = new();

        public TornSeatReadBindingStore(SeatBinding landed) =>
            _ = _inner.TryBindAsync(landed, CancellationToken.None);

        /// <inheritdoc />
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult<SeatBinding?>(null);

        /// <inheritdoc />
        public Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken) =>
            _inner.FindByAccountAsync(gameId, accountId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken) =>
            _inner.ListByGameAsync(gameId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
            IReadOnlyCollection<GameId> gameIds,
            CancellationToken cancellationToken) => _inner.ListByGamesAsync(gameIds, cancellationToken);

        /// <inheritdoc />
        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken) =>
            _inner.TryBindAsync(binding, cancellationToken);

        /// <inheritdoc />
        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            _inner.TryReleaseAsync(gameId, seat, cancellationToken);
    }

    /// <summary>
    /// 过期读假存储：账号那次读看到一条**已经不在表里**的绑定（席位 7 刚被解除），
    /// 而席位表里什么也没有。真实并发里"解除"就发生在两次读之间。
    /// </summary>
    private sealed class StaleAccountReadBindingStore : ISeatBindingStore
    {
        private static readonly SeatBinding Released = new()
        {
            GameId = Game,
            Seat = new SeatId(7),
            AccountId = new AccountId(1),
            BoundAt = Now,
        };

        private readonly FakeBindingStore _inner = new();

        /// <inheritdoc />
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            _inner.FindBySeatAsync(gameId, seat, cancellationToken);

        /// <inheritdoc />
        public Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult<SeatBinding?>(
                Released.GameId == gameId && Released.AccountId == accountId ? Released : null);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken) =>
            _inner.ListByGameAsync(gameId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
            IReadOnlyCollection<GameId> gameIds,
            CancellationToken cancellationToken) => _inner.ListByGamesAsync(gameIds, cancellationToken);

        /// <inheritdoc />
        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken) =>
            _inner.TryBindAsync(binding, cancellationToken);

        /// <inheritdoc />
        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            _inner.TryReleaseAsync(gameId, seat, cancellationToken);
    }

    /// <summary>
    /// 占用刚退场假存储：写入被挡下（<c>false</c>），而两次复核读都读不到占用——
    /// 真实存储里这对应"唯一索引挡下、占用方在复核之前已消失"。
    /// </summary>
    private sealed class VanishingOccupancyBindingStore : ISeatBindingStore
    {
        /// <inheritdoc />
        public Task<SeatBinding?> FindBySeatAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult<SeatBinding?>(null);

        /// <inheritdoc />
        public Task<SeatBinding?> FindByAccountAsync(GameId gameId, AccountId accountId, CancellationToken cancellationToken) =>
            Task.FromResult<SeatBinding?>(null);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGameAsync(GameId gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeatBinding>>([]);

        /// <inheritdoc />
        public Task<IReadOnlyList<SeatBinding>> ListByGamesAsync(
            IReadOnlyCollection<GameId> gameIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SeatBinding>>([]);

        /// <inheritdoc />
        public Task<bool> TryBindAsync(SeatBinding binding, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        /// <inheritdoc />
        public Task<bool> TryReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    /// <summary>固定时钟：绑定时刻断言不依赖真实时间。</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; } = now;
    }
}
