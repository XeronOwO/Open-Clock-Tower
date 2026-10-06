using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 大厅（D-0025）：只有管理员能开桌；任何人都能看到在开的桌；新桌立刻可用且与旧桌隔离。
/// </summary>
/// <remarks>
/// 用真宿主 + 真 SignalR：建桌是"写会话目录 + 装载注册表"的复合动作，
/// 只测服务类会漏掉"写了却装载不了"这类接线错误。
/// </remarks>
public sealed class LobbyHostTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-lobby-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private WebApplicationFactory<Program>? _host;

    /// <summary>管理员登录名（写进宿主配置；D-0025 的授权依据）。</summary>
    private const string AdminUsername = "storyteller-boss";

    public LobbyHostTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _databasePath = Path.Combine(_contentRoot, "lobby.db");
    }

    private WebApplicationFactory<Program> StartHost(params string[] adminUsernames)
    {
        var index = 0;
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", _databasePath);
            builder.UseSetting("GameServer:SeatCount", "5");
            foreach (var username in adminUsernames)
            {
                builder.UseSetting($"GameServer:AdminUsernames:{index}", username);
                index++;
            }
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
        return connection;
    }

    [Fact]
    public async Task CreateTable_ByAdmin_Succeeds_AndTableBecomesUsable()
    {
        StartHost(AdminUsername);
        await using var connection = await ConnectAccountAsync();

        var registered = await connection.InvokeAsync<AccountDto>(
            "Register", AdminUsername, "主持人", "password-123");
        Assert.True(registered.Ok, registered.Message);
        Assert.True(registered.IsAdmin, "配置里的登录名应当被判为管理员");

        var created = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "周三局", 7);

        Assert.True(created.Ok, $"{created.Code}：{created.Message}");
        Assert.Equal(7, created.SeatCount);
        Assert.False(string.IsNullOrWhiteSpace(created.GameId));
        Assert.False(string.IsNullOrWhiteSpace(created.StorytellerTicket));

        // 新桌立刻可用：它在册，且注册表能给出实例（装载失败会在这里暴露）。
        var registry = _host!.Services.GetRequiredService<GameRegistry>();
        Assert.True(registry.Contains(new GameId(created.GameId)));

        // 并且出现在大厅列表里，带着桌名与人数。
        var tables = await connection.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables");
        var table = Assert.Single(tables, item => item.GameId == created.GameId);
        Assert.Equal("周三局", table.Name);
        Assert.Equal(7, table.SeatCapacity);
        Assert.Equal(0, table.TakenSeatCount);
        Assert.False(table.Started);
        Assert.False(table.Locked);
    }

    [Fact]
    public async Task CreateTable_ByOrdinaryPlayer_IsRejected()
    {
        StartHost(AdminUsername);
        await using var connection = await ConnectAccountAsync();

        var registered = await connection.InvokeAsync<AccountDto>(
            "Register", "just-a-player", "路人", "password-123");
        Assert.True(registered.Ok, registered.Message);
        Assert.False(registered.IsAdmin, "不在清单里的登录名不是管理员");

        var created = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "我也想开一桌", 5);

        Assert.False(created.Ok);
        Assert.Equal("not_admin", created.Code);
        Assert.Empty(created.GameId);
        Assert.Null(created.StorytellerTicket);
    }

    [Fact]
    public async Task CreateTable_WithEmptyAdminList_RejectsEveryone()
    {
        // 没配管理员：谁都不能开桌。这是"宁可不给，也不默认放开"的判据。
        StartHost();
        await using var connection = await ConnectAccountAsync();

        var registered = await connection.InvokeAsync<AccountDto>(
            "Register", "anyone", "任何人", "password-123");
        Assert.True(registered.Ok, registered.Message);
        Assert.False(registered.IsAdmin);

        var created = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "空名单", 5);

        Assert.False(created.Ok);
        Assert.Equal("not_admin", created.Code);
    }

    [Fact]
    public async Task CreateTable_WithInvalidSeatCount_IsRejected()
    {
        StartHost(AdminUsername);
        await using var connection = await ConnectAccountAsync();

        var registered = await connection.InvokeAsync<AccountDto>(
            "Register", AdminUsername, "主持人", "password-123");

        var tooMany = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "人太多了", 99);
        var tooFew = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "没人", 0);

        Assert.False(tooMany.Ok);
        Assert.Equal("invalid_seat_count", tooMany.Code);
        Assert.False(tooFew.Ok);
        Assert.Equal("invalid_seat_count", tooFew.Code);
    }

    [Fact]
    public async Task CreateTable_WithInvalidSession_IsRejected()
    {
        StartHost(AdminUsername);
        await using var connection = await ConnectAccountAsync();

        var created = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", "not-a-real-session", "冒名顶替", 5);

        Assert.False(created.Ok);
        Assert.Equal("invalid_session", created.Code);
    }

    [Fact]
    public async Task TwoCreatedTables_AreIndependent()
    {
        StartHost(AdminUsername);
        await using var connection = await ConnectAccountAsync();

        var registered = await connection.InvokeAsync<AccountDto>(
            "Register", AdminUsername, "主持人", "password-123");

        var first = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "甲桌", 5);
        var second = await connection.InvokeAsync<LobbyCreateResultDto>(
            "CreateTable", registered.AccountSession, "乙桌", 6);

        Assert.True(first.Ok, first.Message);
        Assert.True(second.Ok, second.Message);
        Assert.NotEqual(first.GameId, second.GameId);
        Assert.NotEqual(first.StorytellerTicket, second.StorytellerTicket);

        // 两桌各自的席位表独立（不同席位数），且都在册。
        var catalog = _host!.Services.GetRequiredService<IGameCatalog>();
        var setupA = await catalog.FindAsync(new GameId(first.GameId), CancellationToken.None);
        var setupB = await catalog.FindAsync(new GameId(second.GameId), CancellationToken.None);
        Assert.Equal(5, setupA!.Seats.Count);
        Assert.Equal(6, setupB!.Seats.Count);
        Assert.Empty(setupA.Seats.Select(seat => seat.Ticket).Intersect(setupB.Seats.Select(seat => seat.Ticket)));

        var tables = await connection.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables");
        Assert.Contains(tables, item => item.GameId == first.GameId && item.SeatCapacity == 5);
        Assert.Contains(tables, item => item.GameId == second.GameId && item.SeatCapacity == 6);
    }

    public void Dispose()
    {
        _host?.Dispose();

        foreach (var file in new[] { "lobby.db", "lobby.db-shm", "lobby.db-wal" })
        {
            var path = Path.Combine(_contentRoot, file);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // 连接池可能还握着句柄；这类残留由收尾统一清理。
                }
            }
        }

        if (Directory.Exists(_contentRoot) && Directory.GetFileSystemEntries(_contentRoot).Length == 0)
        {
            Directory.Delete(_contentRoot);
        }
    }
}
