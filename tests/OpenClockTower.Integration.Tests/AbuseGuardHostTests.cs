using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 滥用与风控在**真 Hub 链路**上的判据（M4 / G-A5-2 · G-A5-5 · G-A5-6 · G-A5-8 · G-A5-10）。
/// </summary>
/// <remarks>
/// <para>
/// 审计的验收逐条落在这里：注册在额度处停住（含换来源也绕不过的全局额度）、关掉自助注册后
/// 开不出新账号而老账号照常登录、开桌超配额被拒（单账号与全局各一条）、超长 / 含控制字符的
/// 文本被拒而合法文本照旧通行、写文本的频率上限会拦、审计日志带得上操作者与来源且注入不进第二行。
/// </para>
/// <para>
/// 客户端地址由 <see cref="RemoteAddressStartupFilter"/> 摆定（TestServer 的连接是内存里的），
/// 判定仍走产品代码的真链路。
/// </para>
/// </remarks>
public sealed class AbuseGuardHostTests : IDisposable
{
    private const string ClientA = "198.51.100.1";
    private const string ClientB = "198.51.100.2";
    private const string GoodPassword = "password-123";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"oct-abuse-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly List<HubConnection> _connections = [];
    private WebApplicationFactory<Program>? _host;

    public AbuseGuardHostTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _databasePath = Path.Combine(_contentRoot, "abuse.db");
    }

    /// <summary>本用例要覆盖的配置；不给的键**完全不写**（走生产默认值）。</summary>
    private sealed record Settings(
        bool? AllowSelfRegistration = null,
        int? RegisterCallsGlobal = null,
        int? RegisterCallsPerClient = null,
        int? MaxTablesPerAccount = null,
        int? MaxTablesGlobal = null,
        int? WriteTextCallsPerActor = null);

    private WebApplicationFactory<Program> StartHost(Settings? settings = null)
    {
        var effective = settings ?? new Settings();
        _host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("GameServer:DatabasePath", _databasePath);
            builder.UseSetting("GameServer:SeatCount", "5");
            SetIfGiven(builder, "GameServer:AllowSelfRegistration", effective.AllowSelfRegistration);
            SetIfGiven(builder, "GameServer:Throttle:RegisterCallsGlobal", effective.RegisterCallsGlobal);
            SetIfGiven(builder, "GameServer:Throttle:RegisterCallsPerClient", effective.RegisterCallsPerClient);
            SetIfGiven(builder, "GameServer:TableQuota:MaxTablesPerAccount", effective.MaxTablesPerAccount);
            SetIfGiven(builder, "GameServer:TableQuota:MaxTablesGlobal", effective.MaxTablesGlobal);
            SetIfGiven(builder, "GameServer:ActionThrottle:WriteTextCallsPerActor", effective.WriteTextCallsPerActor);
            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(_logs)));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(
                provider => new RemoteAddressStartupFilter(provider.GetRequiredService<IConfiguration>())));
        });

        return _host;
    }

    /// <summary>给了值才写这个键：**null = 完全不写**，也就是"部署方没配它、走生产默认值"那一档。</summary>
    private static void SetIfGiven<TSetting>(IWebHostBuilder builder, string key, TSetting? value)
        where TSetting : struct
    {
        if (value is { } given)
        {
            builder.UseSetting(key, Convert.ToString(given, CultureInfo.InvariantCulture)!);
        }
    }

    /// <summary>开一条账号连接，并把它伪装成来自指定地址的客户端。</summary>
    private async Task<HubConnection> ConnectAccountAsync(string clientAddress = ClientA)
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

    private static Task<AccountDto> LoginAsync(HubConnection connection, string username) =>
        connection.InvokeAsync<AccountDto>("Login", username, GoodPassword);

    private static Task<LobbyCreateResultDto> CreateTableAsync(HubConnection connection, string? accountSession) =>
        connection.InvokeAsync<LobbyCreateResultDto>("CreateTable", accountSession, "压力测试桌", 5);

    private async Task<(HubConnection Connection, string Credential)> JoinStorytellerAsync(
        string gameId,
        string accountSession,
        string clientAddress = ClientA)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_host!.Server.BaseAddress, $"/hub/game?gameId={Uri.EscapeDataString(gameId)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _host.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    options.Headers.Add(RemoteAddressStartupFilter.HeaderName, clientAddress);
                })
            .Build();

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>("JoinStorytellerWithAccount", accountSession);
        _connections.Add(connection);
        return (connection, joined.Credential);
    }

    /// <summary>上报座位状态（说书人在开局前就能做：它是这条链路上最便宜的自由文本入口）。</summary>
    private static Task<CommandResultDto> ReportAsync(
        HubConnection connection,
        string credential,
        string reason,
        string key) =>
        connection.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            credential,
            1,
            "Alive",
            null,
            null,
            null,
            null,
            reason,
            null,
            key);

    [Fact]
    public async Task Registration_GlobalQuota_StopsNewAccounts_EvenFromAnotherAddress()
    {
        // 每来源给的额度很宽（100），只有全局额度是 2：这条用例判的就是"换 IP 也绕不过"。
        StartHost(new Settings(RegisterCallsGlobal: 2, RegisterCallsPerClient: 100));
        var fromA = await ConnectAccountAsync(ClientA);
        var fromB = await ConnectAccountAsync(ClientB);

        Assert.True((await RegisterAsync(fromA, "first-one")).Ok);
        Assert.True((await RegisterAsync(fromB, "second-one")).Ok);

        var blocked = await RegisterAsync(fromB, "third-one");
        Assert.False(blocked.Ok);
        Assert.Equal("too_many_attempts", blocked.Code);
        Assert.Contains("秒后再试", blocked.Message, StringComparison.Ordinal);

        // 反方向：既有账号的登录不受注册额度牵连（限的是"注册"这个入口，不是这台服务器）。
        Assert.True((await LoginAsync(fromB, "second-one")).Ok);
    }

    [Fact]
    public async Task SelfRegistration_CanBeClosed_WithoutLockingOutExistingAccounts()
    {
        StartHost(new Settings(AllowSelfRegistration: true));
        var opening = await ConnectAccountAsync();
        Assert.True((await RegisterAsync(opening, "already-here")).Ok);
        await StopHostAsync();

        // 关掉自助注册再起同一个库：新账号开不出来，老账号照常登录。
        StartHost(new Settings(AllowSelfRegistration: false));
        var reopened = await ConnectAccountAsync();

        var rejected = await RegisterAsync(reopened, "wants-to-join");
        Assert.False(rejected.Ok);
        Assert.Equal("registration_closed", rejected.Code);
        Assert.Contains("不开放自助注册", rejected.Message, StringComparison.Ordinal);

        var existing = await LoginAsync(reopened, "already-here");
        Assert.True(existing.Ok);
        Assert.NotNull(existing.AccountSession);
    }

    [Fact]
    public async Task TableQuota_PerAccount_RejectsTheTableAfterTheLimit()
    {
        StartHost(new Settings(MaxTablesPerAccount: 1));
        var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "table-hog");
        Assert.True(registered.Ok);

        Assert.True((await CreateTableAsync(account, registered.AccountSession)).Ok);

        var blocked = await CreateTableAsync(account, registered.AccountSession);
        Assert.False(blocked.Ok);
        Assert.Equal("table_quota_account", blocked.Code);

        // 反方向：别的账号在**同一个额度**下照样开得出桌（配额是按账号算的，不是全局一份）。
        var other = await RegisterAsync(account, "someone-else");
        Assert.True((await CreateTableAsync(account, other.AccountSession)).Ok);
    }

    [Fact]
    public async Task TableQuota_Global_RejectsTheNextTableAcrossAccounts()
    {
        StartHost(new Settings(MaxTablesGlobal: 1, MaxTablesPerAccount: 10));
        var account = await ConnectAccountAsync();
        var first = await RegisterAsync(account, "first-owner");
        Assert.True((await CreateTableAsync(account, first.AccountSession)).Ok);

        var second = await RegisterAsync(account, "second-owner");
        var blocked = await CreateTableAsync(account, second.AccountSession);
        Assert.False(blocked.Ok);
        Assert.Equal("table_quota_server", blocked.Code);
        Assert.Contains("运维", blocked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeTextLimits_AreEnforcedOverTheWire_WithoutBreakingLegalText()
    {
        StartHost();
        var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "text-limits");
        var gameId = (await CreateTableAsync(account, registered.AccountSession)).GameId;
        var (storyteller, credential) = await JoinStorytellerAsync(gameId, registered.AccountSession!);

        // 合法文本照旧通行（反方向先立住：闸不能把正常用法一起拦掉）。
        var accepted = await ReportAsync(storyteller, credential, "开局前核对生死", "text-ok");
        Assert.Equal("Accepted", accepted.Kind);

        // 换行 / 制表算可折叠空白：放行（落库时折成空格）。
        var multiline = await ReportAsync(storyteller, credential, "第一行\r\n第二行\t结束", "text-multiline");
        Assert.Equal("Accepted", multiline.Kind);

        var tooLong = await ReportAsync(storyteller, credential, new string('甲', 201), "text-too-long");
        Assert.Equal("Rejected", tooLong.Kind);
        Assert.Equal("legality.text_too_long", tooLong.RejectionCode);

        var control = await ReportAsync(storyteller, credential, "甲\u0000乙", "text-control");
        Assert.Equal("Rejected", control.Kind);
        Assert.Equal("legality.text_control_chars", control.RejectionCode);

        // 幂等键也有界：它进数据库主键，是这条闸最要紧的一格。
        var keyTooLong = await ReportAsync(storyteller, credential, "正常原因", new string('k', 65));
        Assert.Equal("Rejected", keyTooLong.Kind);
        Assert.Equal("legality.idempotency_key_too_long", keyTooLong.RejectionCode);

        // 被拒的命令一条事件都没写：视图还在，且后面几步照常可用。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView", credential);
        Assert.NotNull(view);
    }

    [Fact]
    public async Task WriteTextFrequency_StopsTheLoop_AndIsPerIdentity()
    {
        StartHost(new Settings(WriteTextCallsPerActor: 2));
        var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "text-loop");
        var gameId = (await CreateTableAsync(account, registered.AccountSession)).GameId;
        var (storyteller, credential) = await JoinStorytellerAsync(gameId, registered.AccountSession!);

        Assert.Equal("Accepted", (await ReportAsync(storyteller, credential, "第一条", "loop-1")).Kind);
        Assert.Equal("Accepted", (await ReportAsync(storyteller, credential, "第二条", "loop-2")).Kind);

        // 第三次：准入在进会话之前就拦下来（客户端拿到人话，服务端留下审计）。
        var blocked = await Assert.ThrowsAsync<HubException>(
            () => ReportAsync(storyteller, credential, "第三条", "loop-3"));
        Assert.Contains("秒后再试", blocked.Message, StringComparison.Ordinal);
        Assert.Contains("最多 2 次", blocked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientControlledText_CannotForgeASecondLogLine()
    {
        StartHost();
        var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "log-injection");
        var gameId = (await CreateTableAsync(account, registered.AccountSession)).GameId;
        var (storyteller, credential) = await JoinStorytellerAsync(gameId, registered.AccountSession!);

        // 维度值只认枚举名，非法值会带着**客户端原文**进审计日志——正是注入的载体。
        await Assert.ThrowsAsync<HubException>(() => storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            credential,
            1,
            "活着\n[Warning] 伪造的日志行",
            null,
            null,
            null,
            null,
            "正常原因",
            null,
            "injection-1"));

        var line = await WaitForLogAsync(text => text.Contains("命令被拒绝（参数）", StringComparison.Ordinal));
        var escapedNewline = $"活着{Backslash}n";
        Assert.Contains(escapedNewline, line, StringComparison.Ordinal);
        Assert.DoesNotContain("活着\n", line, StringComparison.Ordinal);
    }

    /// <summary>一个反斜杠（把"日志里的转义形态"写成不含长字面量的形状，免得与仓库门禁的字面量规则打架）。</summary>
    private static readonly string Backslash = ((char)92).ToString();

    [Fact]
    public async Task AnnotationUpdate_WithUnchangedText_IsRejected()
    {
        StartHost();
        var account = await ConnectAccountAsync();
        var registered = await RegisterAsync(account, "annotation-loop");
        var gameId = (await CreateTableAsync(account, registered.AccountSession)).GameId;
        var (storyteller, credential) = await JoinStorytellerAsync(gameId, registered.AccountSession!);

        var added = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation", credential, 1, "18 不共边", "annotation-add");
        Assert.Equal("Accepted", added.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView", credential);
        var note = Assert.Single(view.Annotations);

        // 同样的文本改回去：拒绝（否则循环 Update 能写出无上限的事件行，G-A5-8 ②）。
        var unchanged = await storyteller.InvokeAsync<CommandResultDto>(
            "UpdateSeatAnnotation", credential, note.Id, "18 不共边", "annotation-same");
        Assert.Equal("Rejected", unchanged.Kind);
        Assert.Equal("legality.annotation_unchanged", unchanged.RejectionCode);

        // 反方向：真的改了内容就照常接受。
        var changed = await storyteller.InvokeAsync<CommandResultDto>(
            "UpdateSeatAnnotation", credential, note.Id, "18 与 5 不共边", "annotation-changed");
        Assert.Equal("Accepted", changed.Kind);
    }

    [Fact]
    public async Task InviteOnlyAndLogout_AuditLines_CarryOperatorAndSource()
    {
        StartHost();
        var account = await ConnectAccountAsync(ClientA);
        var registered = await RegisterAsync(account, "audited-owner");
        var gameId = (await CreateTableAsync(account, registered.AccountSession)).GameId;
        var (storyteller, credential) = await JoinStorytellerAsync(gameId, registered.AccountSession!, ClientA);

        Assert.True(await storyteller.InvokeAsync<bool>("SetTableInviteOnly", credential, true));

        var accessLine = await WaitForLogAsync(text => text.Contains("桌元数据已更新", StringComparison.Ordinal));
        Assert.Contains("操作者账号=", accessLine, StringComparison.Ordinal);
        Assert.Contains($"客户端={ClientA}", accessLine, StringComparison.Ordinal);

        // 登出同样要能回答"谁从哪来"（M4 / G-A5-10）。
        Assert.True((await account.InvokeAsync<AccountDto>("Logout", registered.AccountSession)).Ok);
        var logoutLine = await WaitForLogAsync(text => text.Contains("已登出", StringComparison.Ordinal));
        Assert.Contains($"客户端={ClientA}", logoutLine, StringComparison.Ordinal);
        Assert.Contains("account=", logoutLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// 全局额度**到顶之后先回收空闲桌再判**（M5 / G-A5-5 容量半边）：上限是"挡住无限堆积"，
    /// 不是"连没人玩的空壳也算数"。
    /// </summary>
    /// <remarks>
    /// 这条判据的两半同样重要：新桌**开得出来**（容量自愈真的发生了），
    /// 而被收掉的是**那一张空闲的老桌**（不是"配额被绕过"、也不是把新桌自己删了）。
    /// 判据取库里的读数与大厅列表，不取回执——回执只证明走到了那一行。
    /// </remarks>
    [Fact]
    public async Task TableQuota_Global_ReclaimsIdleTables_InsteadOfStayingStuck()
    {
        StartHost(new Settings(MaxTablesGlobal: 1, MaxTablesPerAccount: 10));
        var account = await ConnectAccountAsync();
        var first = await RegisterAsync(account, "first-owner");
        var idle = await CreateTableAsync(account, first.AccountSession);
        Assert.True(idle.Ok, idle.Message);

        // 把这一桌改成"两天前开的、没人动过"：它是空桌（没有事件），保留期是 24 小时。
        BackdateTable(idle.GameId, DateTimeOffset.UtcNow.AddDays(-2));

        var second = await RegisterAsync(account, "second-owner");
        var allowed = await CreateTableAsync(account, second.AccountSession);
        Assert.True(allowed.Ok, $"{allowed.Code}：{allowed.Message}");

        Assert.Equal(0, CountRows("Games", idle.GameId));
        Assert.Equal(0, CountRows("Events", idle.GameId));

        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", second.AccountSession);
        Assert.DoesNotContain(tables, table => table.GameId == idle.GameId);
        Assert.Contains(tables, table => table.GameId == allowed.GameId);
    }

    /// <summary>等一条日志出现（日志是异步写的；额度与判据都不变，只是等它落进收集器）。</summary>
    private async Task<string> WaitForLogAsync(Func<string, bool> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var found = _logs.FirstOrDefault(predicate);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"日志里没有匹配的行。已收集：{string.Join(" | ", _logs)}");
        return string.Empty;
    }

    /// <summary>把一桌的建桌时刻改到过去（"这一桌多久没人动了"是时间这一维的输入，夹具只能直接改库）。</summary>
    private void BackdateTable(string gameId, DateTimeOffset createdAt)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET CreatedAt = $when WHERE GameId = $id;";
        command.Parameters.AddWithValue("$when", createdAt);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    /// <summary>这一桌在某张表里还剩几行（回收"删干净了没有"的判据）。</summary>
    private long CountRows(string table, string gameId)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE GameId = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>换一个宿主（同一个库）：用于"关掉自助注册"这类需要改配置的两段式用例。</summary>
    private async Task StopHostAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        _connections.Clear();
        _host?.Dispose();
        _host = null;
    }

    public void Dispose()
    {
        StopHostAsync().GetAwaiter().GetResult();
        SqliteConnection.ClearAllPools();
        TestDatabaseFiles.DeleteOrFail(_databasePath);
        DeleteDirectoryIfEmpty(_contentRoot);
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
        {
            Directory.Delete(path);
        }
    }
}
