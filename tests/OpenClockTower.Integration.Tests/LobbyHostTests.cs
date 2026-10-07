using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 大厅（D-0025 / D-0026 / D-0027）：登录即可开桌，**开桌者成为这一桌的说书人**（凭账号进主持台）；
/// 部署方可以关掉自助开桌，退回"只有运维身份能开"。
/// </summary>
/// <remarks>
/// <para>
/// 用真宿主 + 真 SignalR：开桌是"写会话目录 + 装载注册表"的复合动作，
/// 只测服务类会漏掉"写了却装载不了"这类接线错误。
/// </para>
/// <para>
/// 授权判据（D-0026 / D-0027）最要紧的一条在这里锁住：**"能开桌"不等于"是说书人"**。
/// 所以核心用例不只断言"开桌成功"，还要拿开桌账号真的进一次主持台、并证明**别的账号进不去**
/// ——否则"普通玩家能开桌但进不去"或"谁都能进"这两种半截实现都会假绿。
/// </para>
/// </remarks>
public sealed class LobbyHostTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-lobby-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly List<HubConnection> _connections = [];
    private WebApplicationFactory<Program>? _host;

    /// <summary>运维身份的登录名（写进宿主配置；只在关闭自助开桌时才是开桌依据）。</summary>
    private const string OperatorUsername = "table-operator";

    public LobbyHostTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _databasePath = Path.Combine(_contentRoot, "lobby.db");
    }

    /// <summary>
    /// 起宿主。<paramref name="allowPlayerTables"/> 是部署开关：<c>true</c> / <c>false</c> 显式写该键，
    /// **<c>null</c> = 完全不写这个键**（= 部署方没配它，走 <see cref="GameServerOptions"/> 的默认值）。
    /// </summary>
    /// <remarks>
    /// 那个 null 档是本文件最要紧的一档：D-0026 的门面行为是"**默认放开**"，而"默认"只有在
    /// 不写这个键的时候才被考到——所有用例都显式传 true 的话，把默认值改成 false 也照样全绿。
    /// </remarks>
    private WebApplicationFactory<Program> StartHost(bool? allowPlayerTables, params string[] operatorUsernames)
    {
        var index = 0;
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", _databasePath);
            builder.UseSetting("GameServer:SeatCount", "5");
            if (allowPlayerTables is not null)
            {
                builder.UseSetting("GameServer:AllowPlayerTables", allowPlayerTables.Value ? "true" : "false");
            }

            foreach (var username in operatorUsernames)
            {
                builder.UseSetting($"GameServer:AdminUsernames:{index}", username);
                index++;
            }
        });

        return _host;
    }

    /// <summary>用**单值标量**形态写运维名单（对应 <c>GameServer__AdminUsernames=a,b</c>，环境变量只能这么给）。</summary>
    private WebApplicationFactory<Program> StartHostWithScalarOperatorList(string rawList)
    {
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", _databasePath);
            builder.UseSetting("GameServer:SeatCount", "5");
            builder.UseSetting("GameServer:AllowPlayerTables", "false");
            builder.UseSetting("GameServer:AdminUsernames", rawList);
        });

        return _host;
    }

    /// <summary>开一条账号连接（不落席、只用账号自助与大堂）。</summary>
    private async Task<HubConnection> ConnectAccountAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_host!.Server.BaseAddress, "/hub/account"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// 拿一个**账号会话**去连**指定的桌**并加入主持台（D-0027：身份依据是归属，不是凭据）。
    /// </summary>
    /// <remarks>
    /// 桌通过连接串的 <c>?gameId=</c> 声明——与浏览器端同一机制，所以这条路径是真链路，不是测试旁路。
    /// </remarks>
    private async Task<(HubConnection Connection, string Credential)> ConnectStorytellerAsync(
        string gameId,
        string accountSession)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_host!.Server.BaseAddress, $"/hub/game?gameId={Uri.EscapeDataString(gameId)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>(
            "JoinStorytellerWithAccount",
            accountSession);
        _connections.Add(connection);
        return (connection, joined.Credential);
    }

    private async Task<AccountDto> RegisterAsync(HubConnection connection, string username, string displayName) =>
        await connection.InvokeAsync<AccountDto>("Register", username, displayName, "password-123");

    /// <summary>
    /// 核心用例（D-0026 / D-0027）：**普通玩家**开桌成功，开桌账号真的能进这一桌的主持台，
    /// 而**别的账号进不去**。
    /// </summary>
    /// <remarks>
    /// 这条用例就是需求方那句"说书人是玩这一局的角色，不是系统权限"的运行时判据：
    /// 账号不在任何名单里 → 依然开得出桌 → 依然主持得了；同时它是 D-0027 的判据：
    /// 主持权归属**开桌账号**，不再是一串可以转手的凭据（所以第二段必须红）。
    /// <para>
    /// 传 <c>null</c> 而不是 <c>true</c>：**部署方没配这个键**才是线上默认形态（小圈子自用）。
    /// 写死 true 的话，"把默认值改错"这件事在整套测试里都不会被发现（见 <see cref="StartHost"/> 的说明）。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CreateTable_ByOrdinaryPlayer_Succeeds_AndOwnerAccountOpensThatTableStorytellerConsole()
    {
        // 线上默认形态：没配开关（走默认值）、也没配任何运维身份——最能说明问题的组合。
        StartHost(allowPlayerTables: null);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "just-a-player", "路人甲");
        Assert.True(registered.Ok, registered.Message);
        Assert.True(registered.CanCreateTable, "默认（不配开关）时，任何登录账号都应当能开桌");

        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "路人甲的一桌", 7);

        Assert.True(created.Ok, $"{created.Code}：{created.Message}");
        Assert.Equal(7, created.SeatCount);
        Assert.False(string.IsNullOrWhiteSpace(created.GameId));

        // 关键：他开的桌**就是**他的——用他的账号会话真的主持一下（把这一桌改成邀请制）。
        // `SetTableInviteOnly` 自己会校验"这条凭据是不是本桌说书人的"，所以它成功即证明身份成立。
        var (storyteller, credential) = await ConnectStorytellerAsync(created.GameId, registered.AccountSession!);
        Assert.True(await storyteller.InvokeAsync<bool>("SetTableInviteOnly", credential, true));

        // D-0027 的反方向判据：**另一个账号进不去这一桌的主持台**（不做身份交接 = 没有第二条路）。
        var stranger = await RegisterAsync(account, "not-the-owner", "路人乙");
        Assert.True(stranger.Ok, stranger.Message);
        await Assert.ThrowsAsync<HubException>(
            () => ConnectStorytellerAsync(created.GameId, stranger.AccountSession!));

        // 作用对象必须是**他开的那一桌**：视图里没有桌标识可断言，所以判据走这条闭环——
        // 大厅列表里这一桌是邀请制、他开的**另一桌**不是（连接串里的 gameId 若被吞掉，改的就会是别处）。
        var other = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "路人甲的另一桌", 5);
        Assert.True(other.Ok, other.Message);

        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", registered.AccountSession);
        Assert.True(Assert.Single(tables, item => item.GameId == created.GameId).InviteOnly);
        Assert.False(Assert.Single(tables, item => item.GameId == other.GameId).InviteOnly);

        // 归属由服务端算好（D-0027）：这两桌都是他开的；未登录的人看不到"我的桌"。
        Assert.All(
            tables.Where(item => item.GameId == created.GameId || item.GameId == other.GameId),
            item => Assert.True(item.CreatedByMe));
        var anonymous = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);
        Assert.All(anonymous, item => Assert.False(item.CreatedByMe));

        // 新桌立刻可用：它在册，且注册表能给出实例（装载失败会在这里暴露）。
        var registry = _host!.Services.GetRequiredService<GameRegistry>();
        Assert.True(registry.Contains(new GameId(created.GameId)));

        // 并且出现在大厅列表里，带着桌名与人数。
        var table = Assert.Single(tables, item => item.GameId == created.GameId);
        Assert.Equal("路人甲的一桌", table.Name);
        Assert.Equal(7, table.SeatCapacity);
        Assert.Equal(0, table.TakenSeatCount);
        Assert.False(table.Started);
    }

    /// <summary>关闭自助开桌后：普通玩家被拒，运维身份照常能开。</summary>
    [Fact]
    public async Task CreateTable_WhenPlayerTablesDisabled_RejectsOrdinaryPlayer_ButOperatorSucceeds()
    {
        StartHost(allowPlayerTables: false, OperatorUsername);
        await using var account = await ConnectAccountAsync();

        var player = await RegisterAsync(account, "just-a-player", "路人");
        Assert.False(player.CanCreateTable, "关闭自助开桌后，不在运维名单里的账号不能开桌");

        var rejected = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", player.AccountSession, "我也想开一桌", 5);

        Assert.False(rejected.Ok);
        Assert.Equal("not_allowed", rejected.Code);
        Assert.Empty(rejected.GameId);

        var op = await RegisterAsync(account, OperatorUsername, "运维");
        Assert.True(op.CanCreateTable, "运维身份在关闭自助开桌后仍要能开桌（否则没人开得出第一桌）");

        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", op.AccountSession, "运维开的桌", 5);
        Assert.True(created.Ok, $"{created.Code}：{created.Message}");
    }

    /// <summary>
    /// 运维名单的**单值标量**形态要认（实测踩过：单个标量绑不到 `string[]`，部署时静默失效）。
    /// </summary>
    /// <remarks>
    /// 这一条走的是真正对应 <c>GameServer__AdminUsernames=a,b</c> 的**无索引标量键**，
    /// 也就是 <see cref="AdminDirectory"/> 里 `section.Value` 那条兜底分支——环境变量只能给字符串，
    /// 所以它是部署最常用的形态。上面那条逐名写索引键的用例覆盖不到这里。
    /// </remarks>
    [Fact]
    public async Task CreateTable_WithScalarOperatorList_StillRecognisesOperator()
    {
        StartHostWithScalarOperatorList($"{OperatorUsername},another-operator");
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, OperatorUsername, "运维");
        Assert.True(registered.CanCreateTable, "标量键里逗号分隔的登录名也应当被认成运维身份");

        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "逗号名单", 5);
        Assert.True(created.Ok, $"{created.Code}：{created.Message}");
    }

    /// <summary>关闭自助开桌且**没配运维名单**：谁都不能开（宁可不给，也不默认放开）。</summary>
    [Fact]
    public async Task CreateTable_WhenDisabled_AndNoOperatorConfigured_RejectsEveryone()
    {
        StartHost(allowPlayerTables: false);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "anyone", "任何人");
        Assert.False(registered.CanCreateTable);

        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "空名单", 5);

        Assert.False(created.Ok);
        Assert.Equal("not_allowed", created.Code);
    }

    /// <summary>席位数越界被拒（授权放行之后仍然要守住参数边界）。</summary>
    [Fact]
    public async Task CreateTable_WithInvalidSeatCount_IsRejected()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "seat-boundary", "试边界的人");

        var tooMany = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "人太多了", 99);
        var tooFew = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "没人", 0);

        Assert.False(tooMany.Ok);
        Assert.Equal("invalid_seat_count", tooMany.Code);
        Assert.False(tooFew.Ok);
        Assert.Equal("invalid_seat_count", tooFew.Code);
    }

    /// <summary>桌名过长被拒（边界；授权与席位数都合法时才走到这条）。</summary>
    [Fact]
    public async Task CreateTable_WithOverlongName_IsRejected()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "name-boundary", "试边界的人");
        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, new string('长', 25), 5);

        Assert.False(created.Ok);
        Assert.Equal("invalid_name", created.Code);
        Assert.Empty(created.GameId);
    }

    /// <summary>无效账号会话被拒：开桌要记在某个账号头上，未登录一律不行。</summary>
    [Fact]
    public async Task CreateTable_WithInvalidSession_IsRejected()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();

        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", "not-a-real-session", "冒名顶替", 5);

        Assert.False(created.Ok);
        Assert.Equal("invalid_session", created.Code);
        Assert.Empty(created.GameId);
    }

    /// <summary>被拒的开桌**什么都不落地**：不在会话目录 / 注册表里留半张桌。</summary>
    /// <remarks>
    /// 判据刻意走行为而不是回执字段：只断言"失败回执里 `GameId` 为空"是弱判据——
    /// 那几条断言的是 <c>Fail(...)</c> 这个常量的形状，**"先建了桌再拒"的实现照样全绿**。
    /// 所以这里比的是被拒**前后**的桌数：大厅列表与注册表都不许因为这个请求多出东西来。
    /// </remarks>
    [Fact]
    public async Task RejectedCreate_LeavesNothingBehind()
    {
        StartHost(allowPlayerTables: false);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "no-table-on-failure", "开不出桌的人");
        var registry = _host!.Services.GetRequiredService<GameRegistry>();
        var tablesBefore = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", registered.AccountSession);
        var loadedBefore = registry.GameIds.Count;

        var rejected = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "被拒的桌", 5);

        Assert.False(rejected.Ok);
        Assert.Equal("not_allowed", rejected.Code);
        Assert.Empty(rejected.GameId);
        Assert.Equal(0, rejected.SeatCount);

        var tablesAfter = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", registered.AccountSession);
        Assert.Equal(tablesBefore.Count, tablesAfter.Count);
        Assert.DoesNotContain(tablesAfter, item => item.Name == "被拒的桌");
        Assert.DoesNotContain(tablesAfter, item => item.CreatedByMe);
        Assert.Equal(loadedBefore, registry.GameIds.Count);
    }

    /// <summary>同一账号开两张桌：标识、席位表、归属各自独立，两桌都在册且都归他。</summary>
    [Fact]
    public async Task TwoCreatedTables_AreIndependent()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();

        var registered = await RegisterAsync(account, "two-tables", "开两桌的人");

        var first = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "甲桌", 5);
        var second = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "乙桌", 6);

        Assert.True(first.Ok, first.Message);
        Assert.True(second.Ok, second.Message);
        Assert.NotEqual(first.GameId, second.GameId);

        // 两桌各自的席位表独立（不同席位数），且都在册。
        var catalog = _host!.Services.GetRequiredService<IGameCatalog>();
        var setupA = await catalog.FindAsync(new GameId(first.GameId), CancellationToken.None);
        var setupB = await catalog.FindAsync(new GameId(second.GameId), CancellationToken.None);
        Assert.Equal(5, setupA!.Seats.Count);
        Assert.Equal(6, setupB!.Seats.Count);

        // 邀请码是**签发出来**的，不是建桌时预生成的（D-0038）：两桌各自签同一席，拿到的码必须不同。
        var invitations = _host.Services.GetRequiredService<SeatInvitationService>();
        var codeA = await invitations.IssueAsync(new GameId(first.GameId), new SeatId(1), CancellationToken.None);
        var codeB = await invitations.IssueAsync(new GameId(second.GameId), new SeatId(1), CancellationToken.None);
        Assert.NotEqual(codeA.Code, codeB.Code);

        // 归属（D-0027）：两桌都记在同一个开桌账号名下，两张桌**各自独立**地归他。
        var owner = new AccountId(registered.Id);
        Assert.Equal(owner, setupA.CreatedByAccountId);
        Assert.Equal(owner, setupB.CreatedByAccountId);

        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", registered.AccountSession);
        Assert.Contains(tables, item => item.GameId == first.GameId && item.SeatCapacity == 5 && item.CreatedByMe);
        Assert.Contains(tables, item => item.GameId == second.GameId && item.SeatCapacity == 6 && item.CreatedByMe);
    }

    /// <summary>全新部署：**一张桌都没有**（D-0027：宿主不再自建默认桌，打开站点是空大厅）。</summary>
    /// <remarks>
    /// 这条是"没有'没有房主的桌'"那个判据的正面形式：新库启动后大厅是空的，
    /// 第一桌必须由人在界面上开出来——而不是由引导程序替他开一张没有归属的桌。
    /// </remarks>
    [Fact]
    public async Task FreshDeployment_HasNoTablesAtAll()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();

        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);

        Assert.Empty(tables);
        Assert.Empty(_host!.Services.GetRequiredService<GameRegistry>().GameIds);
    }

    /// <summary>
    /// 旧协议已经**不存在**（D-0027）：直调 `JoinStoryteller(ticket)` 当场失败，不是静默失效。
    /// </summary>
    /// <remarks>
    /// 票据整个退场，所以旧方法不是"留着不宣传"而是被删掉了。这条用例锁住"删干净"：
    /// 只要它还在（哪怕被改成一个什么都不做的空实现），这里就会绿得可疑——所以断言的是**抛错**。
    /// </remarks>
    [Fact]
    public async Task OldTicketProtocol_IsRejected_NotSilentlyIgnored()
    {
        StartHost(allowPlayerTables: true);
        await using var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "protocol-check", "查协议的人");
        var created = await account.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "协议检查桌", 5);
        Assert.True(created.Ok, created.Message);

        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_host!.Server.BaseAddress, $"/hub/game?gameId={Uri.EscapeDataString(created.GameId)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();

        try
        {
            await connection.StartAsync();
            var error = await Assert.ThrowsAnyAsync<Exception>(
                () => connection.InvokeAsync<StorytellerJoinDto>("JoinStoryteller", "storyteller-obsolete"));

            // 失败必须是"这个方法不存在"，而不是"票据不对"——后者说明旧路径还活着。
            Assert.Contains("JoinStoryteller", error.Message);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _host?.Dispose();

        TestDatabaseFiles.Delete(Path.Combine(_contentRoot, "lobby.db"));

        if (Directory.Exists(_contentRoot) && Directory.GetFileSystemEntries(_contentRoot).Length == 0)
        {
            Directory.Delete(_contentRoot);
        }
    }
}
