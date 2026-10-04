using OpenClockTower.Application;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号会话注册表（D-0021）：签发 / 校验 / 登出撤销 / 按账号全撤 / 到期失效。
/// </summary>
public sealed class AccountSessionRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.FromHours(8));

    /// <summary>签发后可校验；未知 / 空凭据一律拒绝。</summary>
    [Fact]
    public void IssueAndResolve_OnlyAcceptsIssuedCredential()
    {
        var registry = new AccountSessionRegistry(new MutableClock(Now));

        var credential = registry.Issue(new AccountId(7));

        Assert.True(registry.TryResolve(credential.Value, out var accountId));
        Assert.Equal(new AccountId(7), accountId);
        Assert.False(registry.TryResolve("不是凭据", out _));
        Assert.False(registry.TryResolve(null, out _));
        Assert.False(registry.TryResolve(string.Empty, out _));
    }

    /// <summary>登出撤销后立即失效；重复撤销返回 false（幂等语义明确）。</summary>
    [Fact]
    public void Revoke_InvalidatesSession()
    {
        var registry = new AccountSessionRegistry(new MutableClock(Now));
        var credential = registry.Issue(new AccountId(1));

        Assert.True(registry.Revoke(credential.Value));
        Assert.False(registry.TryResolve(credential.Value, out _));
        Assert.False(registry.Revoke(credential.Value));
    }

    /// <summary>口令重置撤该账号全部会话：同账号的多条登录都失效，别的账号不受影响。</summary>
    [Fact]
    public void RevokeAllForAccount_OnlyTouchesThatAccount()
    {
        var registry = new AccountSessionRegistry(new MutableClock(Now));
        var first = registry.Issue(new AccountId(1));
        var second = registry.Issue(new AccountId(1));
        var other = registry.Issue(new AccountId(2));

        Assert.Equal(2, registry.RevokeAllForAccount(new AccountId(1)));
        Assert.False(registry.TryResolve(first.Value, out _));
        Assert.False(registry.TryResolve(second.Value, out _));
        Assert.True(registry.TryResolve(other.Value, out var remaining));
        Assert.Equal(new AccountId(2), remaining);
    }

    /// <summary>会话有绝对有效期：到期即失效（不滑动、不需要人工清理）。</summary>
    [Fact]
    public void Session_ExpiresAtLifetime()
    {
        var clock = new MutableClock(Now);
        var registry = new AccountSessionRegistry(clock);
        var credential = registry.Issue(new AccountId(1));

        clock.UtcNow = Now + AccountSessionRegistry.Lifetime - TimeSpan.FromMinutes(1);
        Assert.True(registry.TryResolve(credential.Value, out _));

        clock.UtcNow = Now + AccountSessionRegistry.Lifetime + TimeSpan.FromSeconds(1);
        Assert.False(registry.TryResolve(credential.Value, out _));
    }

    /// <summary>可变时钟：会话到期断言不依赖真实时间。</summary>
    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
