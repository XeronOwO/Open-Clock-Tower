using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 部署形态的宿主契约：**一个进程同时提供页面与接口**（`Program.cs` 的静态文件 + SPA 回退）。
/// </summary>
/// <remarks>
/// 前端产物由 `dotnet publish` 带进发布目录的 `wwwroot`（见 `OpenClockTower.Server.csproj`），
/// 页面与 `/hub` 因此同源，不需要 CORS。本装置把内容根指向一个临时目录并在其中摆一份**假页面**，
/// 判的是路由行为本身——真产物、真发布目录由部署文档里的运行期验证覆盖，两者不重复。
/// <para>
/// 重点在**回退不能吞掉接口**：SPA 回退一旦抢在 Hub 前面，SignalR 的协商会拿到一份 HTML，
/// 浏览器端会以"连不上"告终，而这种故障在页面上看起来只是转圈，很难定位。
/// </para>
/// </remarks>
public sealed class StaticHostingTests : IDisposable
{
    private const string FakeIndexHtml = "<!doctype html><html><body><div id=\"app\">OpenClockTower 假页面</div></body></html>";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-static-{Guid.NewGuid():N}");

    public StaticHostingTests()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), FakeIndexHtml);
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc.js"), "console.log('asset')");
    }

    /// <summary>启动一个内容根指向临时目录的真宿主。</summary>
    private WebApplicationFactory<Program> CreateHost(int seatCount) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", Path.Combine(_contentRoot, "test.db"));
            builder.UseSetting("GameServer:SeatCount", seatCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

    [Fact]
    public async Task Root_ServesFrontPageHtml()
    {
        await using var host = CreateHost(seatCount: 7);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("OpenClockTower 假页面", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepLink_FallsBackToFrontPage()
    {
        // SPA 深链接（前端路由 / 刷新页面）必须拿到同一份 index.html，而不是 404。
        await using var host = CreateHost(seatCount: 5);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/some/deep/link");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StaticAsset_IsServedAsFile()
    {
        await using var host = CreateHost(seatCount: 5);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/assets/index-abc.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("console.log", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFile_IsNotAnsweredWithFrontPage()
    {
        // 带扩展名的路径是"要文件"，不是前端路由：回退约束（nonfile）在这里生效，仍应 404。
        await using var host = CreateHost(seatCount: 5);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/assets/missing-file.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsSeatCountAndIsNotSwallowedByFallback()
    {
        await using var host = CreateHost(seatCount: 7);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/healthz");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 接口不能被 SPA 回退抢走：前端启动时就用这个读数渲染席位名单。
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"seatCount\":7", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HubNegotiate_StillReachesSignalR()
    {
        // 回退接线最危险的一格：协商若被 index.html 顶掉，浏览器只会停在"连不上"。
        await using var host = CreateHost(seatCount: 5);
        using var client = host.CreateClient();

        var response = await client.PostAsync("/hub/game/negotiate?negotiateVersion=1", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("connectionToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenClockTower 假页面", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_StillStartsWithoutFrontendBuild()
    {
        // 未构建前端时（wwwroot 不存在）宿主必须照常起来，只是没有页面：
        // 把 Node 拉进 .NET 构建链是本项目明确不做的（csproj 注释）。
        var bareRoot = Path.Combine(_contentRoot, "no-webroot");
        Directory.CreateDirectory(bareRoot);
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(bareRoot);
            builder.UseSetting("GameServer:DatabasePath", Path.Combine(bareRoot, "test.db"));
            builder.UseSetting("GameServer:SeatCount", "5");
        });
        using var client = host.CreateClient();

        var home = await client.GetAsync("/");
        var health = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.NotFound, home.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.NotNull(host.Services.GetRequiredService<OpenClockTower.Application.IGameCatalog>());
    }

    public void Dispose()
    {
        // 宿主已随 await using 停掉，但本进程的 SQLite 连接池可能还握着库文件的句柄（Windows 上就删不掉）。
        // 这里显式清池而不是"删不掉就算了"：清了还失败才算真的有残留，那时清单一并交给收尾处理。
        SqliteConnection.ClearAllPools();

        // 逐项清掉测试自己造的文件，不做递归删除。
        DeleteIfExists(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc.js"));
        DeleteIfExists(Path.Combine(_contentRoot, "wwwroot", "index.html"));
        DeleteIfExists(Path.Combine(_contentRoot, "test.db"));
        DeleteIfExists(Path.Combine(_contentRoot, "no-webroot", "test.db"));
        DeleteDirectoryIfEmpty(Path.Combine(_contentRoot, "wwwroot", "assets"));
        DeleteDirectoryIfEmpty(Path.Combine(_contentRoot, "wwwroot"));
        DeleteDirectoryIfEmpty(Path.Combine(_contentRoot, "no-webroot"));
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
