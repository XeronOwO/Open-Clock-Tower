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
        // 入座必须登录（D-0037）：空会话与伪造会话都是同一类拒绝，只是文案不同。
        var anonymous = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinTable", string.Empty, 1, 0L));
        Assert.Contains("需要先登录账号", anonymous.Message, StringComparison.Ordinal);

        var forged = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinTable", "伪造账号会话-随机串-不该被认", 1, 0L));
        Assert.Contains("账号会话无效", forged.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 邀请制桌：大厅点不动，但**持邀请码的人进得来**（D-0037；审计 G-A4-2 的正向判据）。
    /// </summary>
    /// <remarks>
    /// G-A4-2 当年把"锁桌不拦票据入座"记成缺陷（Medium）。按新的访问模型那不是缺陷而是设计：
    /// 邀请制桌的语义就是"自助入座被拒、持码者照进"。本用例把**两半都钉住**——
    /// 没码的人被拒、有码的人进来——于是"闸"与"入口"各有一条判据，而不是只有一半。
    /// </remarks>
    [Fact]
    public async Task InviteOnlyTable_RejectsSelfService_ButLetsInviteCodeHolderIn()
    {
        await using var host = new TestServerHost(seatCount: 5);
        var setup = await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");

        // 说书人把这一桌改成邀请制。
        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));

        // 大厅如实说是邀请制（不隐藏、也不是"已锁定"）。
        await using var account = await ConnectAccountAsync(host);
        var listed = Assert.Single(
            await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null),
            item => item.GameId == TableA.Value);
        Assert.True(listed.InviteOnly);

        // ① 没码的人被拒，文案指向邀请码（不是"锁定"）。
        await using var selfService = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => selfService.InvokeAsync<SeatJoinDto>("JoinTable", alice, 2, 0L));
        Assert.Contains("邀请制", rejected.Message, StringComparison.Ordinal);

        // ② 持邀请码的人照进：邀请码 = 说书人给的那一串「桌标识:席位票据」，这里用票据那一段。
        var ticket = setup.Seats.Single(item => item.Seat == new SeatId(2)).Ticket;
        await using var invited = await ConnectTableAsync(host, TableA);
        var joined = await invited.InvokeAsync<SeatJoinDto>("JoinByInviteCode", ticket, alice, 0L);
        Assert.False(string.IsNullOrWhiteSpace(joined.Credential));

        var game = await host.GameRegistry.GetOrCreateAsync(TableA, CancellationToken.None);
        Assert.Equal("爱丽丝", game.SeatNames.NameOf(new SeatId(2)));
    }

    /// <summary>
    /// **开局即关闭自助入座**（D-0037）：已开局的桌服务端一律拒绝新入座——不看界面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一条是本批补上的闸：此前自助入座只判"锁桌"，**从不判已开局**，
    /// 而前端在开局后不显示座位按钮——于是"看不见按钮"就成了唯一的闸。
    /// 用例刻意**直接调 Hub**（不经过任何界面），正是要证明闸在服务端。
    /// </para>
    /// <para>
    /// 反方向同样重要：**本人已认领的那一席在开局之后仍然回得去**——
    /// 刷新即回座不能被开局吃掉（大厅的 <c>mySeatNumbers</c> 与这里是同一个判据）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StartedTable_RejectsSelfServiceJoin_ButKeepsMyOwnSeat()
    {
        await using var host = new TestServerHost(seatCount: 5);
        var setup = await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");
        var bob = await RegisterAsync(host, "bob", "鲍勃");

        await using (var aliceConnection = await ConnectTableAsync(host, TableA))
        {
            await aliceConnection.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);
        }

        await StartFixtureNightAsync(host, TableA, seatCount: 5);

        // 大厅如实说"已开局"（前端据此置灰，服务端另有闸）。
        await using var account = await ConnectAccountAsync(host);
        Assert.True(Assert.Single(
            await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null),
            item => item.GameId == TableA.Value).Started);

        // ① 新人自助入座被服务端拒（不是靠"前端不显示按钮"）。
        await using var bobConnection = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => bobConnection.InvokeAsync<SeatJoinDto>("JoinTable", bob, 2, 0L));
        Assert.Contains("已经开局", rejected.Message, StringComparison.Ordinal);

        // ② 本人那一席照回（回到座位不属于自助入座）。
        await using var aliceAgain = await ConnectTableAsync(host, TableA);
        var back = await aliceAgain.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);
        Assert.False(string.IsNullOrWhiteSpace(back.Credential));

        // ③ 迟到的旅行者由说书人发邀请码进来：邀请码路径不受开局闸影响。
        var ticket = setup.Seats.Single(item => item.Seat == new SeatId(3)).Ticket;
        await using var latecomer = await ConnectTableAsync(host, TableA);
        var invited = await latecomer.InvokeAsync<SeatJoinDto>("JoinByInviteCode", ticket, bob, 0L);
        Assert.False(string.IsNullOrWhiteSpace(invited.Credential));
    }

    /// <summary>把某一桌的开局夹具夜晚开起来（用**那一桌**的会话，不是默认桌）。</summary>
    private static async Task StartFixtureNightAsync(TestServerHost host, GameId gameId, int seatCount)
    {
        var game = await host.GameRegistry.GetOrCreateAsync(gameId, CancellationToken.None);
        var started = await game.Session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
                Actor = Actor.Host,
                IdempotencyKey = $"self-service-start-night:{gameId.Value}",
            },
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);
    }

    [Fact]
    public async Task Lobby_ReportsOccupiedSeats_SoPlayersNeedNotProbe()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");

        await using (var aliceConnection = await ConnectTableAsync(host, TableA))
        {
            await aliceConnection.InvokeAsync<SeatJoinDto>("JoinTable", alice, 3, 0L);
        }

        await using var account = await ConnectAccountAsync(host);
        // 未登录也能看大厅（公开门面）；D-0027 之后这个方法要显式带会话参数，未登录传 null。
        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);
        var table = Assert.Single(tables, item => item.GameId == TableA.Value);

        // 大厅必须如实给出"哪些席位被占"——否则玩家只能点一下试试，撞上才知道被占（实测踩到）。
        Assert.Equal(1, table.TakenSeatCount);
        Assert.Equal([3], table.OccupiedSeatNumbers);
    }

    [Fact]
    public async Task InviteOnlyTable_RejectsNewJoin_ButKeepsExistingPlayers()
    {
        await using var host = new TestServerHost(seatCount: 5);
        await host.RegisterTableAsync(TableA, seatCount: 5);
        var alice = await RegisterAsync(host, "alice", "爱丽丝");
        var bob = await RegisterAsync(host, "bob", "鲍勃");

        // 爱丽丝先坐下。
        await using var aliceConnection = await ConnectTableAsync(host, TableA);
        await aliceConnection.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);

        // 说书人把这一桌改成邀请制。
        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        var inviteOnly = await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true);
        Assert.True(inviteOnly);

        // 新人被挡在门外。
        await using var bobConnection = await ConnectTableAsync(host, TableA);
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => bobConnection.InvokeAsync<SeatJoinDto>("JoinTable", bob, 2, 0L));
        Assert.Contains("邀请制", rejected.Message, StringComparison.Ordinal);

        // 已经在座的人不受影响：她仍能凭账号回到自己的席位。
        await using var aliceAgain = await ConnectTableAsync(host, TableA);
        var back = await aliceAgain.InvokeAsync<SeatJoinDto>("JoinTable", alice, 1, 0L);
        Assert.False(string.IsNullOrWhiteSpace(back.Credential));
    }
}
