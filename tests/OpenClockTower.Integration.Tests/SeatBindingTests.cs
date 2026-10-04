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

    /// <summary>固定时钟：绑定时刻断言不依赖真实时间。</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; } = now;
    }
}
