using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClockTower.Contracts;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 传输面加固在**真宿主管线**上的判据（M3）：安全响应头 / 内容安全策略 / 缓存口径 / 显式上限。
/// </summary>
/// <remarks>
/// <para>
/// G-A3-2 与 G-A3-6 的验收要求"六个头逐个有读数"。这里读的是**应用发出来的**那一份——
/// 反代那一层的读数由真机 <c>curl -I</c> 覆盖（本机没有跑着 nginx，测试不可能证明它）。
/// </para>
/// <para>
/// 上限（G-A3-4）分两段判：① 配置真的被解析成显式值（不吃框架默认）；
/// ② SignalR 的帧上限**运行时真的会拦**——把一个超限的数据帧发上去，服务端必须关连接而不是照单全收。
/// Kestrel 的请求体上限在 TestServer 下没有可运行的判据（它由 Kestrel 而非 TestServer 强制），
/// 那一条由部署后的真机读数覆盖，脚本见 <c>tools/verify-transport-hardening.mjs</c>。
/// </para>
/// </remarks>
public sealed class TransportHardeningHostTests : IDisposable
{
    /// <summary>不是 localhost：HSTS 明确排除 localhost，用它才测得到头。</summary>
    private const string PublicHost = "clocktower.example";

    private const string FakeIndexHtml = "<!doctype html><html><body><div id=\"app\">页面</div></body></html>";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-transport-{Guid.NewGuid():N}");
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly List<HubConnection> _connections = [];

    public TransportHardeningHostTests()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), FakeIndexHtml);
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc.js"), "console.log('asset')");
    }

    /// <summary>起一个内容根指向临时目录的真宿主（与部署形态同构：一个进程同时发页面与接口）。</summary>
    private WebApplicationFactory<Program> CreateHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", Path.Combine(_contentRoot, "transport.db"));
            builder.UseSetting("GameServer:SeatCount", "5");
            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(_logs)));
        });

    private Task<HttpResponseMessage> GetAsync(
        WebApplicationFactory<Program> host,
        string path,
        string scheme = "http",
        string hostName = PublicHost)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{scheme}://{hostName}/"),
        });
        return client.GetAsync(path);
    }

    /// <summary>取一个响应头（普通头与内容头都找）。</summary>
    private static string? HeaderOf(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues)
            ? string.Join(", ", contentValues)
            : null;
    }

    [Fact]
    public async Task HealthEndpoint_CarriesEveryHardeningHeader()
    {
        await using var host = CreateHost();

        using var response = await GetAsync(host, "/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", HeaderOf(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", HeaderOf(response, "Referrer-Policy"));
        Assert.Equal("DENY", HeaderOf(response, "X-Frame-Options"));
        var permissions = Assert.IsType<string>(HeaderOf(response, "Permissions-Policy"));
        Assert.Contains("camera=()", permissions, StringComparison.Ordinal);
        Assert.Contains("microphone=()", permissions, StringComparison.Ordinal);
        Assert.Contains("geolocation=()", permissions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentSecurityPolicy_AllowsOwnAssets_WikiArt_AndSameOriginWebSocket()
    {
        await using var host = CreateHost();

        using var response = await GetAsync(host, "/healthpage"); // SPA 回退也必须是同一套头
        var policy = Assert.IsType<string>(HeaderOf(response, "Content-Security-Policy"));

        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", policy, StringComparison.Ordinal);
        // 角色图是热链的百科图（D-0007 / R-0006）：img-src 不放行它，整套牌面会静默变空白。
        Assert.Contains("img-src 'self' https://clocktower-wiki.gstonegames.com", policy, StringComparison.Ordinal);
        // WebSocket 必须显式列出：部分浏览器不把 'self' 解析到 ws 协议上，而全项目靠它联机。
        Assert.Contains(
            $"connect-src 'self' ws://{PublicHost} wss://{PublicHost}",
            policy,
            StringComparison.Ordinal);
        // 内联脚本 / 样式一概不放：前端的样式是构建期抽出来的文件，运行期只做 CSSOM 赋值。
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentSecurityPolicy_FallsBackToSelfOnly_WhenHostIsNotAHostname()
    {
        // Host 是客户端可控值：不合主机名字面就退回 'self'，不许把任意内容拼进策略里。
        await using var host = CreateHost();

        using var response = await GetAsync(host, "/healthz", hostName: "bad_host.example");
        var policy = Assert.IsType<string>(HeaderOf(response, "Content-Security-Policy"));

        Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("ws://", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashedAssets_AreImmutable_WhileTheShellRevalidates()
    {
        await using var host = CreateHost();

        using var asset = await GetAsync(host, "/assets/index-abc.js");
        using var shell = await GetAsync(host, "/");
        using var deepLink = await GetAsync(host, "/some/deep/link");
        using var health = await GetAsync(host, "/healthz");

        // 产物文件名带内容哈希：换了内容就换 URL，可以放心长缓存。
        Assert.Equal(ResponseHeadersMiddleware.ImmutableCacheControl, HeaderOf(asset, "Cache-Control"));
        // 页面外壳与接口一律先回源校验：外壳被缓存住 = 新版本发布了浏览器还在引旧哈希资源（白屏）。
        Assert.Equal(ResponseHeadersMiddleware.RevalidateCacheControl, HeaderOf(shell, "Cache-Control"));
        Assert.Equal(ResponseHeadersMiddleware.RevalidateCacheControl, HeaderOf(deepLink, "Cache-Control"));
        Assert.Equal(ResponseHeadersMiddleware.RevalidateCacheControl, HeaderOf(health, "Cache-Control"));
    }

    [Fact]
    public async Task HubNegotiate_CarriesTheSameHeaders()
    {
        await using var host = CreateHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{PublicHost}/"),
        });

        using var response = await client.PostAsync("/hub/game/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", HeaderOf(response, "X-Content-Type-Options"));
        Assert.NotNull(HeaderOf(response, "Content-Security-Policy"));
        Assert.Equal(ResponseHeadersMiddleware.RevalidateCacheControl, HeaderOf(response, "Cache-Control"));
    }

    [Fact]
    public async Task Hsts_IsSentOnHttpsResponses_AndNowhereElse()
    {
        await using var host = CreateHost();

        using var overTls = await GetAsync(host, "/healthz", scheme: "https");
        using var plain = await GetAsync(host, "/healthz", scheme: "http");

        var hsts = Assert.IsType<string>(HeaderOf(overTls, "Strict-Transport-Security"));
        Assert.Contains("max-age=", hsts, StringComparison.Ordinal);
        // 不 includeSubDomains：别人的部署可能把本站挂在某个子域上，一条 HSTS 不该管到它的兄弟域。
        Assert.DoesNotContain("includeSubDomains", hsts, StringComparison.Ordinal);
        // 明文响应上不许出现它：浏览器按规范忽略，但写出来会让人误以为"已经有 TLS 了"。
        Assert.Null(HeaderOf(plain, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task OversizedDeclaredBody_IsRejectedBeforeAnyEndpointReadsIt()
    {
        // Kestrel 的 MaxRequestBodySize 只在"应用真的去读体"时生效，而协商端点根本不读体
        // （实测给 /hub/*/negotiate 发 2 MB 照样 200）。所以上限必须有一道按声明长度的前置闸。
        await using var host = CreateHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{PublicHost}/"),
        });

        using var content = new StringContent(new string('x', 300 * 1024));
        using var response = await client.PostAsync("/hub/account/negotiate?negotiateVersion=1", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        // 被拒的响应也要带安全头：拒在响应头中间件之后接线（顺序也是判据）。
        Assert.Equal("nosniff", HeaderOf(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task TransportLimits_AreExplicitValues_NotFrameworkDefaults()
    {
        await using var host = CreateHost();
        var limits = host.Services.GetRequiredService<IOptions<TransportLimitsOptions>>().Value;

        Assert.Equal(256 * 1024, limits.MaxRequestBodyBytes);
        Assert.Equal(64 * 1024, limits.MaxSignalRMessageBytes);
        Assert.Equal(512, limits.MaxConcurrentConnections);
        Assert.Equal(15, limits.RequestHeadersTimeoutSeconds);
        Assert.Equal(60, limits.KeepAliveTimeoutSeconds);

        // SignalR 的消息上限必须真的接进框架选项里（配置解析对了但没接线 = 等于没设）。
        var hub = host.Services.GetRequiredService<IOptions<HubOptions>>().Value;
        Assert.Equal(limits.MaxSignalRMessageBytes, hub.MaximumReceiveMessageSize);
        Assert.Equal(1, hub.MaximumParallelInvocationsPerClient);
        Assert.False(hub.EnableDetailedErrors);
    }

    [Fact]
    public async Task SignalRMessage_OverTheExplicitLimit_IsRejectedAtRuntime()
    {
        await using var host = CreateHost();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "/hub/account"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);
        await connection.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);

        // 64 KB 以上的一帧：服务端必须拒绝并关掉连接，而不是把它当正常命令收下。
        // （若被收下，ListTables 只会返回空列表而不是抛错——所以"连接被关"就是拒绝的判据。）
        var oversized = new string('x', 100 * 1024);
        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => connection.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", oversized));

        Assert.Contains("closed the connection", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);

        // 拒绝必须留下可定位的日志：只看到"连接断了"而不知道为什么，等于没有可观测性。
        // 服务端写日志与客户端收到关闭是两条线，所以这里给它一个有上限的等待（超时即判红）。
        for (var attempt = 0; attempt < 40 && !_logs.Any(IsMessageSizeLog); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.True(
            _logs.Any(IsMessageSizeLog),
            $"服务端没有记下超限原因。日志：{string.Join(" | ", _logs)}");
    }

    /// <summary>服务端"消息超过上限"的那条日志（框架写的，措辞随版本变化，所以按关键词判）。</summary>
    private static bool IsMessageSizeLog(string line) =>
        line.Contains("message size", StringComparison.OrdinalIgnoreCase)
        || line.Contains("65536", StringComparison.Ordinal);

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        SqliteConnection.ClearAllPools();
        DeleteIfExists(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc.js"));
        DeleteIfExists(Path.Combine(_contentRoot, "wwwroot", "index.html"));
        DeleteIfExists(Path.Combine(_contentRoot, "transport.db"));
        DeleteDirectoryIfEmpty(Path.Combine(_contentRoot, "wwwroot", "assets"));
        DeleteDirectoryIfEmpty(Path.Combine(_contentRoot, "wwwroot"));
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
