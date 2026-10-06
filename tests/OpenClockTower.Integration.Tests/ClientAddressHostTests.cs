using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 反代后面的**真实客户端 IP**（M3 / G-A3-3）：<c>X-Forwarded-*</c> 只从可信来源采信，且只取最近的一跳。
/// </summary>
/// <remarks>
/// <para>
/// 判据取"请求日志里记下来的客户端"——那正是限速会用的那个值（<see cref="ClientAddress"/>），
/// 而不是另算一遍的旁路读数。测试用 <see cref="RemoteAddressStartupFilter"/> 摆出"连接对端是谁"，
/// 判定本身仍由产品代码的 <c>ForwardedHeaderPolicy</c> 做。
/// </para>
/// <para>
/// 反方向必须和正方向一样硬：不可信来源伪造的地址**不能**被采信，客户端自带的前缀**不能**影响结果。
/// 少了这两条，"真实 IP"就只是"随便谁写的 IP"。
/// </para>
/// </remarks>
public sealed class ClientAddressHostTests : IDisposable
{
    /// <summary>不是 localhost：HSTS 明确排除 localhost，用它才测得到头。</summary>
    private const string PublicHost = "clocktower.example";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-address-{Guid.NewGuid():N}");
    private readonly ConcurrentQueue<string> _logs = new();

    public ClientAddressHostTests()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));
        File.WriteAllText(
            Path.Combine(_contentRoot, "wwwroot", "index.html"),
            "<!doctype html><html><body>页面</body></html>");
    }

    /// <summary>起一个宿主：连接对端地址由测试摆定，日志收进内存队列。</summary>
    private WebApplicationFactory<Program> CreateHost(string? remoteAddress)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", Path.Combine(_contentRoot, "address.db"));
            builder.UseSetting("GameServer:SeatCount", "5");
            if (remoteAddress is not null)
            {
                builder.UseSetting("Test:RemoteIpAddress", remoteAddress);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(_logs)));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(
                provider => new RemoteAddressStartupFilter(provider.GetRequiredService<IConfiguration>())));
        });
    }

    /// <summary>发一个 GET，并在请求上带若干头。</summary>
    private async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> host,
        string path,
        params (string Name, string Value)[] headers)
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ForwardedFor_FromTrustedProxy_BecomesTheClientAddress()
    {
        // 标准形态：nginx 与宿主同机 ⇒ 连接对端是回环 ⇒ 它追加的 X-Forwarded-For 可信。
        await using var host = CreateHost(remoteAddress: "127.0.0.1");

        await SendAsync(host, "/", ("X-Forwarded-For", "198.51.100.7"));

        Assert.Contains(_logs, line => line.Contains("客户端=198.51.100.7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForwardedFor_FromUntrustedPeer_IsIgnored()
    {
        // 反方向：来源不在可信名单里（这里是"直连的公网地址"），它写什么地址都不算数。
        await using var host = CreateHost(remoteAddress: "203.0.113.9");

        await SendAsync(host, "/", ("X-Forwarded-For", "198.51.100.7"));

        Assert.Contains(_logs, line => line.Contains("客户端=203.0.113.9", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs, line => line.Contains("198.51.100.7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForwardedFor_SpoofedPrefix_IsDropped_LastHopWins()
    {
        // 客户端可以自带一个 X-Forwarded-For：nginx 用 $proxy_add_x_forwarded_for 把真实地址追加在最后，
        // 应用只取最后一段（ForwardLimit = 1），伪造的前缀必须被丢掉。
        await using var host = CreateHost(remoteAddress: "127.0.0.1");

        await SendAsync(host, "/", ("X-Forwarded-For", "192.0.2.99, 198.51.100.7"));

        Assert.Contains(_logs, line => line.Contains("客户端=198.51.100.7", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs, line => line.Contains("192.0.2.99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForwardedProto_FromTrustedProxy_MakesTheRequestCountAsHttps()
    {
        // 反代终止 TLS 时，应用只能靠这个头知道"用户那边其实是 HTTPS"——HSTS 与跳转地址都依赖它。
        await using var host = CreateHost(remoteAddress: "127.0.0.1");

        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{PublicHost}/"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.True(
            response.Headers.Contains("Strict-Transport-Security"),
            "可信代理声明 X-Forwarded-Proto: https 时，响应必须带上 HSTS。");
    }

    [Fact]
    public async Task ForwardedProto_FromUntrustedPeer_IsIgnored()
    {
        await using var host = CreateHost(remoteAddress: "203.0.113.9");

        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{PublicHost}/"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Strict-Transport-Security"),
            "不可信来源声明 X-Forwarded-Proto: https 不该被采信——否则任何人一句头就能让本站以为自己有 TLS。");
    }

    [Fact]
    public void TrustedProxyList_RejectsGarbageAtStartup()
    {
        // 名单写错要炸在启动时：真实 IP 静默失效，会让按 IP 的限速退化成"所有请求同一个桶"。
        var options = new ForwardedHeadersOptions();

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ForwardedHeaderPolicy.Apply(options, ["not-an-address"]);
        });

        Assert.Contains("TrustedProxies", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedProxyList_TakesAddressesAndNetworks()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeaderPolicy.Apply(options, ["10.1.2.3", "172.18.0.0/16"]);

        Assert.Contains(IPAddress.Parse("10.1.2.3"), options.KnownProxies);
        Assert.Contains(options.KnownIPNetworks, network => network.ToString() == "172.18.0.0/16");
        // 回环永远在名单里（同机反代是标准形态），而且只处理最近的一跳。
        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        DeleteIfExists(Path.Combine(_contentRoot, "wwwroot", "index.html"));
        TestDatabaseFiles.DeleteOrFail(Path.Combine(_contentRoot, "address.db"));
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
