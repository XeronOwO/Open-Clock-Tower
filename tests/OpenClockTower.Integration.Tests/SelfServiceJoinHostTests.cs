using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 自助入座（D-0025）：登录后选一个空席位坐下，**不需要任何票据**。
/// </summary>
/// <remarks>
/// <para>
/// 这是本票的用户可见目标：此前玩家必须先从说书人手里拿到一串席位票据才能进门，
/// 账号只能"认领"票据——于是账号系统在体验上等同于不存在。
/// </para>
/// <para>
/// 与隔离票的分工：这里测"能不能自己坐下"，跨桌隔离在
/// <see cref="MultiTableHostIsolationTests"/> 里测。两边都用真宿主 + 真 SignalR。
/// </para>
/// </remarks>
public sealed class SelfServiceJoinHostTests
{
    private static readonly GameId TableA = new("table-a");
    private static readonly GameId TableB = new("table-b");

    /// <summary>注册一个账号，返回它的账号会话。</summary>
    private static async Task<string> RegisterAsync(TestServerHost host, string username, string displayName)
    {
        await using var account = await ConnectAccountAsync(host);
        var registered = await account.InvokeAsync<AccountDto>("Register", username, displayName, "password-123");
        Assert.True(registered.Ok, registered.Message);
        Assert.False(string.IsNullOrWhiteSpace(registered.AccountSession));
        return registered.AccountSession!;
    }

    private static async Task<HubConnection> ConnectAccountAsync(TestServerHost host)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.ServerBaseAddress, "/hub/account"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.ServerHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    /// <summary>开一条连到指定桌的游戏连接（不加入）。</summary>
    private static async Task<HubConnection> ConnectTableAsync(TestServerHost host, GameId gameId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.ServerBaseAddress, $"/hub/game?gameId={gameId.Value}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.ServerHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task PlayerJoinsWithAccountOnly_NoTicketNeeded()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var session = await RegisterAsync(host, "alice", "爱丽丝");

        await using var connection = await ConnectTableAsync(host, TableA);

        // 关键：**没有传任何票据**，只有账号会话 + 想坐的席位号。
        var joined = await connection.InvokeAsync<SeatJoinDto>("JoinTable", session, 3, 0L);

        Assert.False(string.IsNullOrWhiteSpace(joined.Credential));
        Assert.NotNull(joined.Bundle);

        // 席位与玩家名都落到了甲桌的读模型上。
        var game = await host.GameRegistry.GetOrCreateAsync(TableA, CancellationToken.None);
        Assert.Equal("爱丽丝", game.SeatNames.NameOf(new SeatId(3)));
    }

    [Fact]
    public async Task SameAccount_CanSitInTwoTables_WithIndependentSeats()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        await host.RegisterTableAsync(TableB, seatCount: 6);
        var session = await RegisterAsync(host, "bob", "鲍勃");

        await using var connectionA = await ConnectTableAsync(host, TableA);
        await using var connectionB = await ConnectTableAsync(host, TableB);

        await connectionA.InvokeAsync<SeatJoinDto>("JoinTable", session, 1, 0L);
        // 同一账号在另一桌可以再坐一席：席位绑定按桌隔离（SeatBindings 主键是 (GameId, Seat)）。
        await connectionB.InvokeAsync<SeatJoinDto>("JoinTable", session, 2, 0L);

        var gameA = await host.GameRegistry.GetOrCreateAsync(TableA, CancellationToken.None);
        var gameB = await host.GameRegistry.GetOrCreateAsync(TableB, CancellationToken.None);

        Assert.Equal("鲍勃", gameA.SeatNames.NameOf(new SeatId(1)));
        Assert.Null(gameA.SeatNames.NameOf(new SeatId(2)));

        Assert.Equal("鲍勃", gameB.SeatNames.NameOf(new SeatId(2)));
        // 反方向：甲桌的席位名不出现在乙桌。
        Assert.Null(gameB.SeatNames.NameOf(new SeatId(1)));
    }

    [Fact]
    public async Task SeatTakenByAnotherAccount_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");
        var bob = await RegisterAsync(host, "bob", "鲍勃");

        await using (var first = await ConnectTableAsync(host, TableA))
        {
            await first.InvokeAsync<SeatJoinDto>("JoinTable", alice, 2, 0L);
        }

        await using var second = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => second.InvokeAsync<SeatJoinDto>("JoinTable", bob, 2, 0L));

        Assert.Contains("2", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameAccountClaimingTwoSeatsInOneTable_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var session = await RegisterAsync(host, "carol", "卡罗尔");

        await using var first = await ConnectTableAsync(host, TableA);
        await first.InvokeAsync<SeatJoinDto>("JoinTable", session, 1, 0L);

        await using var second = await ConnectTableAsync(host, TableA);
        // 一账号一桌一席：第二席必须被拒（D-0021 的不变量，自助路径同样受它约束）。
        await Assert.ThrowsAsync<HubException>(
            () => second.InvokeAsync<SeatJoinDto>("JoinTable", session, 4, 0L));
    }

    [Fact]
    public async Task SeatOutOfRange_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var session = await RegisterAsync(host, "dave", "戴夫");

        await using var connection = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinTable", session, 9, 0L));

        Assert.Contains("5", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoiningWithoutAccount_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);

        await using var connection = await ConnectTableAsync(host, TableA);
        // 自助入座必须登录；游客仍应走票据路径（那条路径没有被拆掉）。
        await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinTable", string.Empty, 1, 0L));
    }

    [Fact]
    public async Task LockedTable_RejectsNewJoin_ButKeepsExistingPlayers()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");
        var bob = await RegisterAsync(host, "bob", "鲍勃");

        // 爱丽丝先坐下。
        await using var aliceConnection = await ConnectTableAsync(host, TableA);
        await aliceConnection.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);

        // 说书人锁桌。
        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        var locked = await storyteller.Raw.InvokeAsync<bool>("SetTableLock", storyteller.Credential, true);
        Assert.True(locked);

        // 新人被挡在门外。
        await using var bobConnection = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => bobConnection.InvokeAsync<SeatJoinDto>("JoinTable", bob, 2, 0L));
        Assert.Contains("锁定", rejected.Message, StringComparison.Ordinal);

        // 已经在座的人不受影响：她仍能凭账号回到自己的席位。
        await using var aliceAgain = await ConnectTableAsync(host, TableA);
        var back = await aliceAgain.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);
        Assert.False(string.IsNullOrWhiteSpace(back.Credential));
    }
}
