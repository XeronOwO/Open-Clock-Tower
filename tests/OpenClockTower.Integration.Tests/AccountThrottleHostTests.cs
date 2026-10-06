using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClockTower.Contracts;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号入口限速在**真 Hub 链路**上的判据（M4 / G-A1-1）：爆破被拦，而正常用户不受牵连。
/// </summary>
/// <remarks>
/// <para>
/// 审计（G-A1-1）给的验收是"连续失败 N 次后第 N+1 次被拒，且正确口令不受影响、不误伤同 IP 的其他账号"。
/// 这里逐条判：被拒时的结果码与人话、**成功路径在同地址被锁时依然从别处可用**（不锁账号）、
/// 不同登录名不受牵连、注册与重置也被限。
/// </para>
/// <para>
/// 客户端地址由 <see cref="RemoteAddressStartupFilter"/> 摆定（TestServer 的连接是内存里的，
/// 默认没有对端地址），限速本身仍走产品代码的真链路——包括反代头归一之后的
/// <c>ClientAddress</c>。
/// </para>
/// </remarks>
public sealed class AccountThrottleHostTests : IDisposable
{
    private const string ClientA = "198.51.100.1";
    private const string ClientB = "198.51.100.2";
    private const string GoodPassword = "password-123";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-throttle-{Guid.NewGuid():N}");
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly List<HubConnection> _connections = [];
    private WebApplicationFactory<Program>? _host;

    public AccountThrottleHostTests()
    {
        Directory.CreateDirectory(_contentRoot);
    }

    /// <summary>起宿主；只覆盖本用例真正关心的那几个阈值，其余走生产默认值。</summary>
    private WebApplicationFactory<Program> StartHost(
        int? loginFailuresPerUsername = null,
        int? loginFailuresPerClient = null,
        int? registerCallsPerClient = null)
    {
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", Path.Combine(_contentRoot, "throttle.db"));
            builder.UseSetting("GameServer:SeatCount", "5");
            SetIfGiven(builder, "GameServer:Throttle:LoginFailuresPerUsername", loginFailuresPerUsername);
            SetIfGiven(builder, "GameServer:Throttle:LoginFailuresPerClient", loginFailuresPerClient);
            SetIfGiven(builder, "GameServer:Throttle:RegisterCallsPerClient", registerCallsPerClient);
            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(_logs)));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(
                provider => new RemoteAddressStartupFilter(provider.GetRequiredService<IConfiguration>())));
        });

        return _host;
    }

    private static void SetIfGiven(IWebHostBuilder builder, string key, int? value)
    {
        if (value is { } number)
        {
            builder.UseSetting(key, number.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>开一条账号连接，并把它伪装成来自指定地址的客户端。</summary>
    private async Task<HubConnection> ConnectAsync(string clientAddress)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_host!.Server.BaseAddress, "/hub/account"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add(RemoteAddressStartupFilter.HeaderName, clientAddress);
            })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    private static Task<AccountDto> RegisterAsync(HubConnection connection, string username) =>
        connection.InvokeAsync<AccountDto>("Register", username, "测试玩家", GoodPassword);

    private static Task<AccountDto> LoginAsync(HubConnection connection, string username, string password) =>
        connection.InvokeAsync<AccountDto>("Login", username, password);

    [Fact]
    public async Task RepeatedWrongPasswords_GetRejected_WhileTheAccountStillWorksFromElsewhere()
    {
        StartHost();
        var fromA = await ConnectAsync(ClientA);
        var fromB = await ConnectAsync(ClientB);
        Assert.True((await RegisterAsync(fromA, "alice")).Ok);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await LoginAsync(fromA, "alice", "not-the-password");
            Assert.False(wrong.Ok);
            Assert.Equal("invalid_credentials", wrong.Code);
        }

        // 第 6 次：**即使口令是对的**也被拦——锁的是"这个地址在猜这个登录名"这件事。
        var locked = await LoginAsync(fromA, "alice", GoodPassword);
        Assert.False(locked.Ok);
        Assert.Equal("too_many_attempts", locked.Code);
        Assert.Contains("秒后再试", locked.Message, StringComparison.Ordinal);

        // 反方向：别的地址用正确口令照样进得来——限速不是"把账号锁死"。
        var elsewhere = await LoginAsync(fromB, "alice", GoodPassword);
        Assert.True(elsewhere.Ok);
        Assert.NotNull(elsewhere.AccountSession);
    }

    [Fact]
    public async Task FailedLogins_AgainstDifferentUsernames_HitTheAddressBudget()
    {
        StartHost(loginFailuresPerUsername: 100, loginFailuresPerClient: 3);
        var fromA = await ConnectAsync(ClientA);
        var fromB = await ConnectAsync(ClientB);

        foreach (var username in new[] { "alice", "bob", "carol" })
        {
            Assert.Equal("invalid_credentials", (await LoginAsync(fromA, username, "whatever-123")).Code);
        }

        // 换着名字猜也要停（否则"跨登录名"那个桶形同虚设）。
        Assert.Equal("too_many_attempts", (await LoginAsync(fromA, "dave", "whatever-123")).Code);
        // 别的地址不受影响。
        Assert.Equal("invalid_credentials", (await LoginAsync(fromB, "dave", "whatever-123")).Code);
    }

    [Fact]
    public async Task Registration_IsLimitedPerAddress_AndSuccessDoesNotRefund()
    {
        StartHost(registerCallsPerClient: 2);
        var fromA = await ConnectAsync(ClientA);
        var fromB = await ConnectAsync(ClientB);

        Assert.True((await RegisterAsync(fromA, "first-one")).Ok);
        Assert.True((await RegisterAsync(fromA, "second-one")).Ok);

        var blocked = await RegisterAsync(fromA, "third-one");
        Assert.False(blocked.Ok);
        Assert.Equal("too_many_attempts", blocked.Code);

        // 别的地址照常注册（拦的是刷注册的那个来源，不是所有人）。
        Assert.True((await RegisterAsync(fromB, "someone-else")).Ok);
    }

    [Fact]
    public async Task RepeatedWrongRecoveryCodes_GetRejected_WhileTheRealCodeStillWorksElsewhere()
    {
        StartHost();
        var fromA = await ConnectAsync(ClientA);
        var fromB = await ConnectAsync(ClientB);
        var registered = await RegisterAsync(fromA, "reset-me");
        Assert.True(registered.Ok);
        var recoveryCode = Assert.IsType<string>(registered.RecoveryCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await fromA.InvokeAsync<AccountDto>(
                "ResetPassword",
                "reset-me",
                "not-the-recovery-code",
                "password-456");
            Assert.False(wrong.Ok);
        }

        var locked = await fromA.InvokeAsync<AccountDto>(
            "ResetPassword",
            "reset-me",
            recoveryCode,
            "password-456");
        Assert.Equal("too_many_attempts", locked.Code);

        // 反方向：真正的恢复码从别处用得了——恢复码不是被"猜废"的。
        var recovered = await fromB.InvokeAsync<AccountDto>(
            "ResetPassword",
            "reset-me",
            recoveryCode,
            "password-456");
        Assert.True(recovered.Ok);
    }

    [Fact]
    public async Task LoginRejection_LogsTheRealClientAddress()
    {
        StartHost();
        var fromA = await ConnectAsync(ClientA);

        await LoginAsync(fromA, "nobody-here", GoodPassword);

        for (var attempt = 0; attempt < 40 && !_logs.Any(LineWithClientAddress); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.True(
            _logs.Any(LineWithClientAddress),
            $"登录失败的日志里没有真实客户端地址。日志：{string.Join(" | ", _logs)}");

        bool LineWithClientAddress(string line) =>
            line.Contains("登录被拒", StringComparison.Ordinal)
            && line.Contains($"客户端={ClientA}", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        SqliteConnection.ClearAllPools();
        DeleteIfExists(Path.Combine(_contentRoot, "throttle.db"));
        DeleteDirectoryIfEmpty(_contentRoot);
    }

    private static void DeleteIfExists(string path)
    {
        Assert.False(File.Exists(path) && !TryDelete(path), $"测试残留文件删不掉，需要收尾清理：{path}");
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
        {
            Directory.Delete(path);
        }
    }
}
