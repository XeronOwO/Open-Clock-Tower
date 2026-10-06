using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号入口限速的计数口径（M4 / G-A1-1）：窗口、成功清零、两个键互不牵连、内存有界。
/// </summary>
/// <remarks>
/// 时钟是注入的（<see cref="IClock"/>），所以"窗口过期"可以当场判，不用真的等 5 分钟；
/// 而"按 IP + 登录名两个桶"这类口径分歧，在这里逐个钉死。
/// </remarks>
public sealed class AccountAttemptLimiterTests
{
    private const string Client = "198.51.100.1";
    private const string OtherClient = "198.51.100.2";

    [Fact]
    public void Login_LocksThePair_ThenReleasesItWhenTheWindowExpires()
    {
        var clock = new MutableClock();
        var limiter = Create(new ThrottleOptions(), clock);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(limiter.Check(ThrottleAction.Login, Client, "alice").Allowed);
            limiter.RecordAttempt(ThrottleAction.Login, Client, "alice");
        }

        var locked = limiter.Check(ThrottleAction.Login, Client, "alice");
        Assert.False(locked.Allowed);
        Assert.InRange(locked.RetryAfterSeconds, 1, 300);

        // 窗口一过自动放行：不需要谁来"解锁"，也就不存在"把人永久锁在门外"的状态。
        clock.Advance(TimeSpan.FromSeconds(301));

        Assert.True(limiter.Check(ThrottleAction.Login, Client, "alice").Allowed);
    }

    [Fact]
    public void Login_SuccessClearsTheFailures()
    {
        var clock = new MutableClock();
        var limiter = Create(new ThrottleOptions(), clock);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            limiter.RecordAttempt(ThrottleAction.Login, Client, "alice");
        }

        limiter.RecordSuccess(ThrottleAction.Login, Client, "alice");

        // 成功之后重新开始计数：正常用户打错几次、然后登进来，不该背着旧账。
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.True(limiter.Check(ThrottleAction.Login, Client, "alice").Allowed);
            limiter.RecordAttempt(ThrottleAction.Login, Client, "alice");
        }
    }

    [Fact]
    public void Login_BucketsAreIndependentPerClientAndPerUsername()
    {
        var clock = new MutableClock();
        var limiter = Create(new ThrottleOptions(), clock);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            limiter.RecordAttempt(ThrottleAction.Login, Client, "alice");
        }

        // 换登录名（同一客户端）仍放行：只按地址会把整个 NAT 连坐。
        Assert.True(limiter.Check(ThrottleAction.Login, Client, "bob").Allowed);
        // 换客户端（同一登录名）也放行：别人打不锁我的账号——锁的是"打的那个人"。
        Assert.True(limiter.Check(ThrottleAction.Login, OtherClient, "alice").Allowed);
    }

    [Fact]
    public void Login_ClientBudget_CountsFailuresAcrossUsernames()
    {
        var options = new ThrottleOptions { LoginFailuresPerClient = 3, LoginFailuresPerUsername = 100 };
        var clock = new MutableClock();
        var limiter = Create(options, clock);

        foreach (var username in new[] { "alice", "bob", "carol" })
        {
            limiter.RecordAttempt(ThrottleAction.Login, Client, username);
        }

        // 换着名字试也要停：这正是"跨登录名"那个桶存在的理由。
        Assert.False(limiter.Check(ThrottleAction.Login, Client, "dave").Allowed);
        Assert.True(limiter.Check(ThrottleAction.Login, OtherClient, "dave").Allowed);
    }

    [Fact]
    public void Register_BudgetIsNotClearedBySuccess()
    {
        var options = new ThrottleOptions { RegisterCallsPerClient = 2 };
        var limiter = Create(options, new MutableClock());

        limiter.RecordAttempt(ThrottleAction.Register, Client, username: null);
        limiter.RecordSuccess(ThrottleAction.Register, Client, username: null);
        limiter.RecordAttempt(ThrottleAction.Register, Client, username: null);

        // 注册成功不清零：清了就等于"注册多少都行"，那正是这条要限制的东西。
        Assert.False(limiter.Check(ThrottleAction.Register, Client, username: null).Allowed);
        Assert.True(limiter.Check(ThrottleAction.Register, OtherClient, username: null).Allowed);
    }

    [Fact]
    public void Buckets_AreBounded_SoRandomUsernamesCannotGrowMemory()
    {
        var options = new ThrottleOptions { MaxTrackedBuckets = 4, LoginFailuresPerUsername = 100 };
        var limiter = Create(options, new MutableClock());

        for (var attempt = 0; attempt < 200; attempt++)
        {
            limiter.RecordAttempt(ThrottleAction.Login, Client, $"user-{attempt}");
        }

        Assert.InRange(limiter.TrackedBuckets, 1, 4);
    }

    [Fact]
    public void ExpiredBuckets_ArePruned_SoNewKeysAreStillTracked()
    {
        var options = new ThrottleOptions { MaxTrackedBuckets = 4, WindowSeconds = 60 };
        var clock = new MutableClock();
        var limiter = Create(options, clock);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            limiter.RecordAttempt(ThrottleAction.Login, Client, $"user-{attempt}");
        }

        Assert.InRange(limiter.TrackedBuckets, 1, 4);

        // 窗口全过期之后，清理要腾出位置：否则"桶满"会变成永久的（新键再也不被单独计数）。
        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.RecordAttempt(ThrottleAction.Login, OtherClient, "fresh");

        Assert.True(limiter.TrackedBuckets >= 1);
        Assert.True(limiter.Check(ThrottleAction.Login, OtherClient, "fresh").Allowed);
    }

    private static AccountAttemptLimiter Create(ThrottleOptions options, MutableClock clock) =>
        new(Options.Create(options), clock, NullLogger<AccountAttemptLimiter>.Instance);

    /// <summary>可推进的时钟：窗口过期不必真等。</summary>
    private sealed class MutableClock : IClock
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        /// <inheritdoc />
        public DateTimeOffset UtcNow => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
