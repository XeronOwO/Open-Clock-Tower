using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 连接登记表的撤销面（M2 / G-A2-1）：按会话撤、按账号撤，以及"撤干净"的边界——
/// 凭据不再被受理、席位 / 主持路由一起消失（推送跟着停）、游客不受账号撤销牵连。
/// </summary>
public sealed class ConnectionRegistryTests
{
    private static readonly GameId FirstTable = new("default");
    private static readonly GameId SecondTable = new("second");

    /// <summary>按会话撤席位连接：凭据与席位路由一起消失——只删凭据会留下推送的后门。</summary>
    [Fact]
    public void RevokeSession_ClearsCredentialAndSeatRoute()
    {
        var registry = new ConnectionRegistry();
        var session = AccountSessionRef.CreateNew(new AccountId(1));
        var credential = registry.IssueForSeat(FirstTable, new SeatId(1), "conn-1", session);
        Assert.True(registry.Validate(credential, "conn-1").Accepted);

        var revoked = registry.RevokeSession(session);

        Assert.Single(revoked);
        Assert.Equal("conn-1", revoked[0]);
        Assert.False(registry.Validate(credential, "conn-1").Accepted);
        Assert.False(registry.TryGetSeatConnection(FirstTable, new SeatId(1), out _));
        Assert.Empty(registry.SeatsOf(FirstTable));
    }

    /// <summary>按会话撤主持连接：主持权一起消失（枚举里不再有它，说书人推送不会漏给撤过的连接）。</summary>
    [Fact]
    public void RevokeSession_ClearsStorytellerRoute()
    {
        var registry = new ConnectionRegistry();
        var session = AccountSessionRef.CreateNew(new AccountId(1));
        var credential = registry.IssueForStoryteller(FirstTable, "conn-host", session);
        Assert.Contains("conn-host", registry.StorytellerConnectionsOf(FirstTable));

        var revoked = registry.RevokeSession(session);

        Assert.Single(revoked);
        Assert.Equal("conn-host", revoked[0]);
        Assert.False(registry.Validate(credential, "conn-host").Accepted);
        Assert.Empty(registry.StorytellerConnectionsOf(FirstTable));
    }

    /// <summary>精确性：撤一条会话不碰同账号的另一条会话，也不碰那条会话在别桌的连接。</summary>
    [Fact]
    public void RevokeSession_OnlyTouchesThatSession()
    {
        var registry = new ConnectionRegistry();
        var account = new AccountId(7);
        var first = AccountSessionRef.CreateNew(account);
        var second = AccountSessionRef.CreateNew(account);

        var firstSeat = registry.IssueForSeat(FirstTable, new SeatId(1), "conn-first", first);
        var secondSeat = registry.IssueForSeat(SecondTable, new SeatId(2), "conn-second", second);

        var revoked = registry.RevokeSession(first);

        Assert.Single(revoked);
        Assert.Equal("conn-first", revoked[0]);
        Assert.False(registry.Validate(firstSeat, "conn-first").Accepted);
        Assert.True(registry.Validate(secondSeat, "conn-second").Accepted);
    }

    /// <summary>按账号撤：该账号的**每一条**会话的连接一起撤（口令重置的覆盖面）。</summary>
    [Fact]
    public void RevokeAccount_CoversEverySessionOfThatAccount()
    {
        var registry = new ConnectionRegistry();
        var account = new AccountId(7);
        var first = AccountSessionRef.CreateNew(account);
        var second = AccountSessionRef.CreateNew(account);
        var stranger = AccountSessionRef.CreateNew(new AccountId(8));

        var firstSeat = registry.IssueForSeat(FirstTable, new SeatId(1), "conn-first", first);
        var secondHost = registry.IssueForStoryteller(SecondTable, "conn-second", second);
        var strangerSeat = registry.IssueForSeat(FirstTable, new SeatId(2), "conn-stranger", stranger);

        var revoked = registry.RevokeAccount(account);

        Assert.Equal(2, revoked.Count);
        Assert.False(registry.Validate(firstSeat, "conn-first").Accepted);
        Assert.False(registry.Validate(secondHost, "conn-second").Accepted);
        Assert.True(registry.Validate(strangerSeat, "conn-stranger").Accepted);
    }

    /// <summary>反方向：游客连接（只凭票据、没有账号会话）不受任何账号级撤销牵连。</summary>
    [Fact]
    public void RevokeAccount_LeavesGuestConnectionAlone()
    {
        var registry = new ConnectionRegistry();
        var guest = registry.IssueForSeat(FirstTable, new SeatId(3), "conn-guest", session: null);

        Assert.Empty(registry.RevokeAccount(new AccountId(7)));
        Assert.True(registry.Validate(guest, "conn-guest").Accepted);
    }

    /// <summary>边界：撤一条不存在的会话（或重复撤）是空操作，不报错也不误伤别人。</summary>
    [Fact]
    public void Revoke_ForUnknownSession_IsNoOp()
    {
        var registry = new ConnectionRegistry();
        var known = AccountSessionRef.CreateNew(new AccountId(1));
        var unknown = AccountSessionRef.CreateNew(new AccountId(1));
        var credential = registry.IssueForSeat(FirstTable, new SeatId(1), "conn-1", known);

        Assert.Empty(registry.RevokeSession(unknown));
        Assert.Empty(registry.RevokeAccount(new AccountId(99)));
        Assert.True(registry.Validate(credential, "conn-1").Accepted);

        // 重复撤同一条：第二次已经是空操作。
        Assert.Single(registry.RevokeSession(known));
        Assert.Empty(registry.RevokeSession(known));
    }
}
