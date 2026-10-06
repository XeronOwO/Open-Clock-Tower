using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 撤销覆盖面（M2 / G-A2-1）：登出 / 口令重置**打到已经进门的那条连接上**，而且只打该打的那几条。
/// </summary>
/// <remarks>
/// <para>
/// M1 把撤销做到了"下一次进门"：登出 / 改口令之后 <c>AccountHub.Resume</c> 与重新 Join 都会被拒。
/// 但连接级凭据一旦签发就与账号会话脱钩——**已经进门的连接继续有效**，直到刷新 / 关页 / 断线。
/// 本文件是那一刀的回归面：每条"允许"都配一条反方向（别的账号、别的会话、游客都不受牵连）。
/// </para>
/// <para>
/// 判据统一取"命令是否还在凭据闸里被拒"：撤销前拒绝来自领域闸（返回 DTO 或别的文案），
/// 撤销后必须是凭据闸的 <c>连接凭据无效</c>。只看文案不看类型，是为了不把失败路径写死。
/// </para>
/// </remarks>
public sealed class SessionRevocationHostTests
{
    /// <summary>凭据闸被关上的证据：文案是 <see cref="ConnectionGateMessage"/>。</summary>
    private const string ConnectionGateMessage = "连接凭据无效";

    /// <summary>席位连接的账号登出后，**同一连接**上的命令立刻被凭据闸拒绝。</summary>
    [Fact]
    public async Task Logout_RejectsCommandsOnLiveSeatConnection()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var alice = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var seat = await host.ConnectSeatAsync(new SeatId(1), accountSession: alice.AccountSession);

        // 撤销之前：这条连接过得了凭据闸（拒绝可能来自领域闸，那不是这里要看的）。
        Assert.False(await IsRejectedByConnectionGateAsync(seat, "g-a2-1-before"));

        Assert.True((await account.InvokeAsync<AccountDto>("Logout", alice.AccountSession)).Ok);

        Assert.True(await IsRejectedByConnectionGateAsync(seat, "g-a2-1-after"));
    }

    /// <summary>主持连接的账号登出后，说书人查询在同一连接上立刻被拒（席位之外的整条撤销面）。</summary>
    [Fact]
    public async Task Logout_RejectsQueriesOnLiveStorytellerConnection()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var owner = host.OwnerOf(TestServerHost.GameId);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.NotNull(await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView"));

        var account = await host.ConnectAccountAsync();
        Assert.True((await account.InvokeAsync<AccountDto>("Logout", owner.AccountSession)).Ok);

        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView"));
        Assert.Contains(ConnectionGateMessage, rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>口令重置撤该账号全部会话，也就撤掉该账号的全部在线连接（换口令 = 全场踢下线）。</summary>
    [Fact]
    public async Task ResetPassword_RejectsLiveSeatConnection()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var alice = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var seat = await host.ConnectSeatAsync(new SeatId(1), accountSession: alice.AccountSession);
        Assert.False(await IsRejectedByConnectionGateAsync(seat, "g-a2-1-reset-before"));

        var reset = await account.InvokeAsync<AccountDto>(
            "ResetPassword",
            "alice",
            alice.RecoveryCode!,
            "password-456");
        Assert.True(reset.Ok);

        Assert.True(await IsRejectedByConnectionGateAsync(seat, "g-a2-1-reset-after"));
    }

    /// <summary>
    /// 反方向（精确性）：登出**只撤这条会话**——同一账号在别处的另一条会话（另一台设备）不受牵连。
    /// </summary>
    /// <remarks>
    /// 撤销过头的症状是"我在手机上登出，笔记本上的牌局被踢了"，而那条会话本来还是有效的：
    /// 撤销面必须与会话一一对应，不然修复本身就成了新缺陷。
    /// </remarks>
    [Fact]
    public async Task Logout_OnlyRevokesConnectionsOfThatSession()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var owner = host.OwnerOf(TestServerHost.GameId);
        await using var firstDevice = await host.ConnectStorytellerAsync();

        // 第二台设备：同一账号再登录一次，拿到另一条会话，去主持另一张桌。
        var account = await host.ConnectAccountAsync();
        var secondLogin = await TestServerHost.LoginAccountAsync(account, owner.Username, owner.Password);
        Assert.True(secondLogin.Ok);
        var otherTable = new GameId("second-table");
        await host.RegisterTableAsync(otherTable, 3, owner with { AccountSession = secondLogin.AccountSession! });
        await using var secondDevice = await host.ConnectStorytellerToTableAsync(otherTable);

        // 只登出第一台设备的那条会话。
        Assert.True((await account.InvokeAsync<AccountDto>("Logout", owner.AccountSession)).Ok);

        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            firstDevice.InvokeAsync<StorytellerViewDto>("GetStorytellerView"));
        Assert.Contains(ConnectionGateMessage, rejected.Message, StringComparison.Ordinal);

        Assert.NotNull(await secondDevice.InvokeAsync<StorytellerViewDto>("GetStorytellerView"));
    }

    /// <summary>反方向（隔离）：游客席位只凭票据入座，没有账号可撤——账号登出不该碰到它。</summary>
    [Fact]
    public async Task Logout_DoesNotTouchGuestConnection()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var alice = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var aliceSeat = await host.ConnectSeatAsync(new SeatId(1), accountSession: alice.AccountSession);
        await using var guest = await host.ConnectSeatAsync(new SeatId(2));

        Assert.True((await account.InvokeAsync<AccountDto>("Logout", alice.AccountSession)).Ok);

        Assert.True(await IsRejectedByConnectionGateAsync(aliceSeat, "g-a2-1-account-after"));
        Assert.False(await IsRejectedByConnectionGateAsync(guest, "g-a2-1-guest-after"));
    }

    /// <summary>
    /// 这条连接的凭据是不是已经被凭据闸拒了：命令可能被领域闸拒（那是 DTO 或别的文案），
    /// 只有凭据闸的拒绝才算"撤销打到了这条连接上"。
    /// </summary>
    private static async Task<bool> IsRejectedByConnectionGateAsync(GameClient client, string idempotencyKey)
    {
        try
        {
            await client.InvokeAsync<CommandResultDto>("Nominate", 2, idempotencyKey);
            return false;
        }
        catch (HubException exception)
        {
            return exception.Message.Contains(ConnectionGateMessage, StringComparison.Ordinal);
        }
    }
}
