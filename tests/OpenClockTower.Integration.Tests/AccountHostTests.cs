using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号与玩家名在真实宿主里的链路（D-0021）：注册 / 登录 / 票据认领 / 只凭账号重连 /
/// 改名即时同步 / 恢复码重置 / 说书人解除绑定 / 重启后仍在。
/// </summary>
public sealed class AccountHostTests
{
    /// <summary>注册 → 认领 → 双端看到同一份「席位 → 玩家名」；游客席位没有名字。</summary>
    [Fact]
    public async Task RegisterClaim_ShowsNameToEveryone_AndGuestStaysNameless()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "Alice", "爱丽丝", "password-123");
        Assert.True(registered.Ok);
        Assert.False(string.IsNullOrEmpty(registered.AccountSession));
        Assert.False(string.IsNullOrEmpty(registered.RecoveryCode));

        await using var alice = await host.ConnectSeatAsync(new SeatId(1), accountSession: registered.AccountSession);
        var aliceBundle = host.Bundles[new SeatId(1)];
        Assert.Contains(aliceBundle.View.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝");

        // 同桌的游客（只凭票据）也收到同一份公开映射；他自己的席位没有名字。
        await using var guest = await host.ConnectSeatAsync(new SeatId(2));
        var guestBundle = host.Bundles[new SeatId(2)];
        Assert.Contains(guestBundle.View.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝");
        Assert.DoesNotContain(guestBundle.View.SeatNames, item => item.Seat == 2);

        // 说书人看到同一份（D-0021 矩阵 M1 行 2）。
        await using var storyteller = await host.ConnectStorytellerAsync();
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Contains(view.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝");
    }

    /// <summary>一席一账号、一账号一席：抢别人的席位 / 一个账号占两席都被显式拒绝。</summary>
    [Fact]
    public async Task Claim_IsExclusivePerSeatAndPerAccount()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var alice = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var aliceSeat = await host.ConnectSeatAsync(new SeatId(1), accountSession: alice.AccountSession);

        var bob = await TestServerHost.RegisterAccountAsync(account, "bob", "鲍勃", "password-123");
        var taken = await Assert.ThrowsAsync<HubException>(() =>
            host.ConnectSeatAsync(new SeatId(1), accountSession: bob.AccountSession));
        Assert.Contains("已经由其他账号认领", taken.Message, StringComparison.Ordinal);

        await using var bobSeat = await host.ConnectSeatAsync(new SeatId(2), accountSession: bob.AccountSession);
        var secondSeat = await Assert.ThrowsAsync<HubException>(() =>
            host.ConnectSeatAsync(new SeatId(3), accountSession: bob.AccountSession));
        Assert.Contains("已经认领了席位 2", secondSeat.Message, StringComparison.Ordinal);
    }

    /// <summary>账号会话无效显式拒绝，不静默降级成游客（否则"名字没显示"会变成静默故障）。</summary>
    [Fact]
    public async Task Join_WithInvalidAccountSession_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 3);

        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            host.ConnectSeatAsync(new SeatId(1), accountSession: "伪造的账号会话"));

        Assert.Contains("账号会话无效", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>认领之后可以只凭账号重连（不带票据），服务端按绑定解出席位。</summary>
    [Fact]
    public async Task ClaimedSeat_CanRejoinWithAccountOnly()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var first = await host.ConnectSeatAsync(new SeatId(1), accountSession: registered.AccountSession);

        await using var rejoined = await host.ConnectSeatByAccountAsync(registered.AccountSession!);

        var bundle = host.Bundles[new SeatId(1)];
        Assert.Contains(bundle.View.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝");
    }

    /// <summary>未认领席位时"只凭账号"加入被拒（先要带票据认领一次）。</summary>
    [Fact]
    public async Task JoinByAccountOnly_WithoutBinding_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");

        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            host.ConnectSeatByAccountAsync(registered.AccountSession!));

        Assert.Contains("还没有认领席位", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>改名即时同步：账号改玩家名 → 已绑定席位的玩家视图与说书人视图一起更新。</summary>
    [Fact]
    public async Task ChangeDisplayName_PropagatesToPlayerAndStorytellerViews()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");

        var playerViews = new List<PlayerViewDto>();
        await using var alice = await host.ConnectSeatAsync(
            new SeatId(1),
            accountSession: registered.AccountSession,
            onPlayerViewChanged: (_, view) => playerViews.Add(view));
        var storytellerViews = new List<StorytellerViewDto>();
        await using var storyteller = await host.ConnectStorytellerAsync(storytellerViews.Add);

        var renamed = await account.InvokeAsync<AccountDto>("ChangeDisplayName", registered.AccountSession, "爱丽丝二世");
        Assert.True(renamed.Ok);
        Assert.Equal("爱丽丝二世", renamed.DisplayName);

        var playerSaw = await TestServerHost.WaitUntilAsync(() =>
            playerViews.Any(view => view.SeatNames.Any(item => item.Seat == 1 && item.DisplayName == "爱丽丝二世")));
        Assert.True(playerSaw, "玩家没有收到改名后的席位名推送");

        var storytellerView = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.SeatNames.Any(item => item.Seat == 1 && item.DisplayName == "爱丽丝二世"));
        Assert.Contains(storytellerView!.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝二世");
        Assert.DoesNotContain(storytellerView.SeatNames, item => item.DisplayName == "爱丽丝");
    }

    /// <summary>恢复码重置口令：旧会话与旧口令失效、恢复码轮换、新会话立即可用。</summary>
    [Fact]
    public async Task ResetPassword_RotatesRecoveryCode_AndRevokesOldSessions()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        var originalSession = registered.AccountSession!;
        var originalCode = registered.RecoveryCode!;

        var wrong = await account.InvokeAsync<AccountDto>("ResetPassword", "alice", "错误恢复码", "password-456");
        Assert.False(wrong.Ok);
        Assert.Equal("invalid_recovery", wrong.Code);

        var reset = await account.InvokeAsync<AccountDto>("ResetPassword", "alice", originalCode, "password-456");
        Assert.True(reset.Ok);
        Assert.NotEqual(originalCode, reset.RecoveryCode);
        Assert.False(string.IsNullOrEmpty(reset.AccountSession));

        var oldSessionRejected = await Assert.ThrowsAsync<HubException>(() =>
            host.ConnectSeatByAccountAsync(originalSession));
        Assert.Contains("账号会话无效", oldSessionRejected.Message, StringComparison.Ordinal);

        var oldPassword = await TestServerHost.LoginAccountAsync(account, "alice", "password-123");
        Assert.False(oldPassword.Ok);
        var newPassword = await TestServerHost.LoginAccountAsync(account, "alice", "password-456");
        Assert.True(newPassword.Ok);
        var oldCode = await account.InvokeAsync<AccountDto>("ResetPassword", "alice", originalCode, "password-789");
        Assert.False(oldCode.Ok);
    }

    /// <summary>
    /// 会话恢复（M1 / D-0029）：有效会话换回资料与能力位、**不重发凭据**；
    /// 伪造的、登出后的旧凭据一律被拒——`sessionStorage` 里那一份也不例外。
    /// </summary>
    [Fact]
    public async Task Resume_ReturnsProfileWithoutCredential_AndRejectsRevokedSessions()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        var session = registered.AccountSession!;

        var resumed = await account.InvokeAsync<AccountDto>("Resume", session);
        Assert.True(resumed.Ok);
        Assert.Equal("alice", resumed.Username);
        Assert.Equal("爱丽丝", resumed.DisplayName);
        Assert.Equal(registered.CanCreateTable, resumed.CanCreateTable);
        Assert.Null(resumed.AccountSession);
        Assert.Null(resumed.RecoveryCode);

        // 反方向：伪造的会话换不到任何资料，也不透露账号是否存在。
        var forged = await account.InvokeAsync<AccountDto>("Resume", "伪造的账号会话");
        Assert.False(forged.Ok);
        Assert.Equal("invalid_session", forged.Code);

        Assert.True((await account.InvokeAsync<AccountDto>("Logout", session)).Ok);
        var afterLogout = await account.InvokeAsync<AccountDto>("Resume", session);
        Assert.False(afterLogout.Ok);
        Assert.Equal("invalid_session", afterLogout.Code);
    }

    /// <summary>
    /// 口令重置之后（M1 口径 4）：**旧会话恢复被拒、新会话可恢复**。
    /// 前端把旧凭据留在 `sessionStorage` 里也没用——撤销由服务端说了算。
    /// </summary>
    [Fact]
    public async Task Resume_AfterPasswordReset_RejectsOldSession_AndAcceptsNewOne()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        var oldSession = registered.AccountSession!;

        var reset = await account.InvokeAsync<AccountDto>(
            "ResetPassword",
            "alice",
            registered.RecoveryCode!,
            "password-456");
        Assert.True(reset.Ok);
        var newSession = reset.AccountSession!;
        Assert.NotEqual(oldSession, newSession);

        var rejected = await account.InvokeAsync<AccountDto>("Resume", oldSession);
        Assert.False(rejected.Ok);
        Assert.Equal("invalid_session", rejected.Code);

        var accepted = await account.InvokeAsync<AccountDto>("Resume", newSession);
        Assert.True(accepted.Ok);
        Assert.Equal("alice", accepted.Username);
    }

    /// <summary>说书人可解除席位绑定（误认领兜底）；解除后名字消失、席位可被其他账号重新认领。</summary>
    [Fact]
    public async Task ReleaseSeatBinding_ClearsName_AndFreesTheSeat()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var alice = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var aliceSeat = await host.ConnectSeatAsync(new SeatId(1), accountSession: alice.AccountSession);
        await using var storyteller = await host.ConnectStorytellerAsync();

        // 玩家连接不能解除绑定（身份闸）。
        await using var guest = await host.ConnectSeatAsync(new SeatId(2));
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<bool>("ReleaseSeatBinding", 1));

        Assert.True(await storyteller.InvokeAsync<bool>("ReleaseSeatBinding", 1));
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.DoesNotContain(view.SeatNames, item => item.Seat == 1);
        Assert.False(await storyteller.InvokeAsync<bool>("ReleaseSeatBinding", 1));

        var bob = await TestServerHost.RegisterAccountAsync(account, "bob", "鲍勃", "password-123");
        await using var bobSeat = await host.ConnectSeatAsync(new SeatId(1), accountSession: bob.AccountSession);
        Assert.Contains(
            host.Bundles[new SeatId(1)].View.SeatNames,
            item => item.Seat == 1 && item.DisplayName == "鲍勃");
    }

    /// <summary>
    /// 复盘读侧带同一份玩家名（D-0021）：说书人实时面能看到「N 号 · 玩家名」文案——
    /// 关闭 `replay-auto-review` 的「复盘文案仍是席位号」残余。
    /// </summary>
    [Fact]
    public async Task ReplayView_CarriesSeatNames_AndNameAwareCopy()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
        await using var seat = await host.ConnectSeatAsync(new SeatId(1), accountSession: registered.AccountSession);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var reported = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Alive",
            null,
            null,
            null,
            null,
            "测试：复盘玩家名",
            null,
            "test-account-replay-1");
        Assert.Equal("Accepted", reported.Kind);

        var replay = await storyteller.InvokeAsync<ReplayViewDto>("GetReplay", 0, 100);

        Assert.Contains(replay.SeatNames, item => item.Seat == 1 && item.DisplayName == "爱丽丝");
        Assert.Contains(
            replay.Steps,
            step => step.Summary.Contains("1 号 · 爱丽丝", StringComparison.Ordinal));
    }

    /// <summary>重启后账号 / 绑定仍在，读模型重新装载出玩家名（M1 行 4 / M2 行 4）。</summary>
    [Fact]
    public async Task Restart_KeepsAccountBinding_AndReloadsSeatNames()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-account-restart-{Guid.NewGuid():N}.db");
        try
        {
            await using (var host = new TestServerHost(databasePath: databasePath, deleteDatabaseOnDispose: false))
            {
                var account = await host.ConnectAccountAsync();
                var registered = await TestServerHost.RegisterAccountAsync(account, "alice", "爱丽丝", "password-123");
                await using var seat = await host.ConnectSeatAsync(new SeatId(1), accountSession: registered.AccountSession);
            }

            await using (var restarted = new TestServerHost(databasePath: databasePath, deleteDatabaseOnDispose: false))
            {
                var account = await restarted.ConnectAccountAsync();
                var login = await TestServerHost.LoginAccountAsync(account, "alice", "password-123");
                Assert.True(login.Ok);
                var seat = await restarted.ConnectSeatByAccountAsync(login.AccountSession!);
                Assert.Contains(
                    restarted.Bundles[new SeatId(1)].View.SeatNames,
                    item => item.Seat == 1 && item.DisplayName == "爱丽丝");
                await seat.DisposeAsync();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestDatabaseFiles.Delete(databasePath);
        }
    }
}
