using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 一个真实宿主（WebApplicationFactory + TestServer）与真实 SignalR 客户端的测试装置。
/// </summary>
/// <remarks>
/// 验收规程要求关键链路在**真实运行**里被证明：这里跑的是真宿主、真 Hub 协议与真 SQLite
/// （临时文件），而不是直接调用领域方法。生产宿主不再自动开阶段（占位计划已移除），
/// 需要步骤机的用例由本装置按需开启一个**测试夹具**夜晚（<see cref="TestNightPlan"/>）。
/// <para>
/// 零信任（D-0012）：客户端一律用 <see cref="GameClient"/>——它持有 Join 时下发的连接级凭据，
/// 每条命令自动出示；负向用例改用 <see cref="GameClient.InvokeRawAsync"/> 或裸连接。
/// </para>
/// </remarks>
public sealed class TestServerHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databasePath;
    private readonly bool _deleteDatabaseOnDispose;
    private readonly List<HubConnection> _connections = [];
    private readonly Dictionary<GameId, FixtureAccount> _owners = [];
    private FixtureAccounts? _fixtures;

    /// <summary>夹具账号的签发台（入座必须登录，D-0037；按席位缓存账号）。</summary>
    private FixtureAccounts Fixtures => _fixtures ??= new FixtureAccounts(_factory.Services);

    /// <summary>启动一个测试宿主。</summary>
    /// <param name="slotQuotaSeconds">槽位配额（秒）。</param>
    /// <param name="seatCount">席位数量（同时用于票据播种与测试夹具计划）。</param>
    /// <param name="databasePath">SQLite 路径；null 时用临时文件。</param>
    /// <param name="deleteDatabaseOnDispose">销毁时是否删除库文件。</param>
    /// <param name="autoStartTestNight">
    /// 启动后是否开启测试夹具夜晚。恢复失败等待显式重开的场景传 false。
    /// </param>
    public TestServerHost(
        double slotQuotaSeconds = 3600,
        int seatCount = 3,
        string? databasePath = null,
        bool deleteDatabaseOnDispose = true,
        bool autoStartTestNight = true)
    {
        _databasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"oct-test-{Guid.NewGuid():N}.db");
        _deleteDatabaseOnDispose = deleteDatabaseOnDispose;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GameServer:DatabasePath", _databasePath);
            builder.UseSetting("GameServer:SlotQuotaSeconds", slotQuotaSeconds.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("GameServer:SeatCount", seatCount.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("GameServer:PacerIntervalMilliseconds", "50");
            // 夹具会把注册与登录调用很多次，而限速的键在 TestServer 下是"未知地址"这一个桶
            // （连接是内存里的，没有对端 IP）。**限速本身由 AccountThrottleHostTests 用生产值单独判**，
            // 这里只是不让夹具的正常往返被它误伤。
            builder.UseSetting("GameServer:Throttle:RegisterCallsPerClient", "1000");
            builder.UseSetting("GameServer:Throttle:LoginFailuresPerClient", "1000");
            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(Logs)));
        });

        // 触发宿主启动：建库、结构迁移与核对、装载在册的桌（D-0027 之后**宿主不再自建任何桌**）。
        // 起不来时把工厂销毁掉：M5 / G-A6-3 之后宿主还握着库的单实例锁，
        // 不销毁的话后面几步（删库 / 删锁文件）会全部失败——而"起不来"正是本类用例要判的场景之一。
        try
        {
            _ = _factory.Services;

            // 夹具自己把默认桌开出来，并把它记在一个夹具账号名下——进主持台只认这个账号（D-0027）。
            RegisterTableAsync(GameId, seatCount).GetAwaiter().GetResult();

            // 按需开启测试夹具夜晚：已有阶段（重启恢复等场景）不重开，保持与旧引导行为一致
            if (autoStartTestNight && Session.GetStorytellerView().Phase is null)
            {
                ExecuteHostCommandAsync(
                    new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
                    "test-bootstrap:test-night-1",
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch
        {
            _factory.Dispose();
            throw;
        }
    }

    /// <summary>宿主服务容器。</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>本宿主用的 SQLite 文件（备份 / 体检类用例要指着真实的库文件做）。</summary>
    public string DatabasePath => _databasePath;

    /// <summary>局注册表（多桌用例要按标识取具体某一桌，D-0024）。</summary>
    public GameRegistry GameRegistry => _factory.Services.GetRequiredService<GameRegistry>();

    /// <summary>
    /// 默认桌的实例束（多桌，D-0024）。
    /// </summary>
    /// <remarks>
    /// 会话与席位名读模型都不是进程级单例了——它们每局一份、由 <see cref="GameRegistry"/> 持有。
    /// 测试要拿"当前这一局"必须走注册表，否则会拿到一个**从未被恢复**的实例
    /// （重启类用例会以"状态丢了"的形式红）。
    /// </remarks>
    public GameInstance Game =>
        _factory.Services.GetRequiredService<GameRegistry>()
            .FindAsync(GameId, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
        ?? throw new InvalidOperationException("测试宿主没有装载默认桌：启动引导未完成？");

    /// <summary>本局编排器（测试用来从宿主侧开阶段）。</summary>
    public GameSession Session => Game.Session;

    /// <summary>事件存储（测试用来读事件与审计）。</summary>
    public IGameStore Store => _factory.Services.GetRequiredService<IGameStore>();

    /// <summary>宿主日志（负向套件用来断言"每次拒绝都有可定位的审计"）。</summary>
    public ConcurrentQueue<string> Logs { get; } = new();

    /// <summary>默认游戏标识。</summary>
    public static GameId GameId => new("default");

    /// <summary>最近一次加入得到的重连包。</summary>
    public ConcurrentDictionary<SeatId, ReconnectBundleDto> Bundles { get; } = new();

    /// <summary>读取会话票据。</summary>
    public async Task<GameSetup> GetSetupAsync() =>
        await _factory.Services.GetRequiredService<IGameCatalog>().FindAsync(GameId, CancellationToken.None)
        ?? throw new InvalidOperationException("测试宿主尚未播种会话票据");

    /// <summary>
    /// 以某席位加入（可挂收件回调）；返回带凭据的客户端。
    /// </summary>
    /// <param name="seat">席位号。</param>
    /// <param name="accountSession">
    /// 账号会话；null = 就地注册一个夹具账号（**入座必须登录**，D-0037：游客那条路径已整个删除）。
    /// 同一席位的夹具账号会复用——重复调用同席（重连类用例）不会撞登录名。
    /// </param>
    public async Task<GameClient> ConnectSeatAsync(
        SeatId seat,
        Action<OperationRequestDto>? onRequest = null,
        Action<OperationRequestVoidedDto>? onVoided = null,
        long lastSequence = 0,
        Action<StorytellerViewDto>? onStorytellerView = null,
        Action<InformationResultDto>? onInformation = null,
        Action<OperationRequestAnsweredDto>? onAnswered = null,
        Action<PhaseStartedDto>? onPhaseStarted = null,
        Action<long, PlayerViewDto>? onPlayerViewChanged = null,
        string? accountSession = null)
    {
        var setup = await GetSetupAsync();
        var ticket = setup.Seats.Single(item => item.Seat == seat).Ticket;
        var connection = CreateConnection();
        if (onRequest is not null)
        {
            connection.On<OperationRequestDto>("ReceiveOperationRequest", onRequest);
        }

        if (onInformation is not null)
        {
            connection.On<InformationResultDto>("ReceiveInformationResult", onInformation);
        }

        if (onVoided is not null)
        {
            connection.On<OperationRequestVoidedDto>("ReceiveOperationRequestVoided", onVoided);
        }

        if (onAnswered is not null)
        {
            connection.On<OperationRequestAnsweredDto>("ReceiveOperationRequestAnswered", onAnswered);
        }

        if (onPhaseStarted is not null)
        {
            connection.On<PhaseStartedDto>("ReceivePhaseStarted", onPhaseStarted);
        }

        if (onPlayerViewChanged is not null)
        {
            connection.On<long, PlayerViewDto>("ReceivePlayerViewChanged", onPlayerViewChanged);
        }

        if (onStorytellerView is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onStorytellerView);
        }

        await connection.StartAsync();
        var session = accountSession ?? (await SeatFixtureAccountAsync(seat)).AccountSession;
        var joined = await connection.InvokeCoreAsync<SeatJoinDto>(
            "JoinByInviteCode",
            [ticket, session, lastSequence]);
        Bundles[seat] = joined.Bundle;
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>
    /// 这一席的**夹具账号**（就地注册并缓存；入座必须登录，D-0037）。签发台在 <see cref="FixtureAccounts"/>。
    /// </summary>
    public Task<FixtureAccount> SeatFixtureAccountAsync(SeatId seat) => Fixtures.ForSeatAsync(seat);

    /// <summary>只凭账号加入（D-0021 / D-0037）：不带票据，服务端按绑定解出席位（"认领之后的重连"路径）。</summary>
    public async Task<GameClient> ConnectSeatByAccountAsync(string accountSession, long lastSequence = 0)
    {
        var connection = CreateConnection();
        await connection.StartAsync();
        var joined = await connection.InvokeCoreAsync<SeatJoinDto>(
            "JoinByInviteCode",
            [string.Empty, accountSession, lastSequence]);
        Bundles[new SeatId(joined.Bundle.View.Seat)] = joined.Bundle;
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>
    /// 以某席位凭邀请码加入**指定的那一桌**（多桌用例用；可挂访问模式推送回调，D-0037）。
    /// </summary>
    /// <param name="gameId">哪一桌（连接串里的 <c>?gameId=</c>）。</param>
    /// <param name="seat">席位号。</param>
    /// <param name="onAccess">访问模式推送回调（null = 不挂）。</param>
    public async Task<GameClient> ConnectSeatToTableAsync(
        GameId gameId,
        SeatId seat,
        Action<TableAccessDto>? onAccess = null)
    {
        var setup = await _factory.Services.GetRequiredService<IGameCatalog>().FindAsync(gameId, CancellationToken.None)
            ?? throw new InvalidOperationException($"这张桌还没有会话信息：{gameId.Value}");
        var ticket = setup.Seats.Single(item => item.Seat == seat).Ticket;
        var connection = CreateConnection($"/hub/game?gameId={Uri.EscapeDataString(gameId.Value)}");
        if (onAccess is not null)
        {
            connection.On<TableAccessDto>("ReceiveTableAccessChanged", onAccess);
        }

        await connection.StartAsync();
        var session = (await SeatFixtureAccountAsync(seat)).AccountSession;
        var joined = await connection.InvokeCoreAsync<SeatJoinDto>(
            "JoinByInviteCode",
            [ticket, session, 0L]);
        Bundles[seat] = joined.Bundle;
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>起一条账号 Hub 连接（D-0021）：注册 / 登录 / 登出 / 改名 / 口令重置用。</summary>
    public async Task<HubConnection> ConnectAccountAsync()
    {
        var connection = CreateConnection("/hub/account");
        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>注册账号（D-0021）：返回账号结果（含一次性恢复码与账号会话）。</summary>
    public static Task<AccountDto> RegisterAccountAsync(
        HubConnection account,
        string username,
        string displayName,
        string password) =>
        account.InvokeAsync<AccountDto>("Register", username, displayName, password);

    /// <summary>登录（D-0021）。</summary>
    public static Task<AccountDto> LoginAccountAsync(HubConnection account, string username, string password) =>
        account.InvokeAsync<AccountDto>("Login", username, password);

    /// <summary>以说书人身份加入默认桌（房主账号的会话，D-0027）；返回带凭据的客户端。</summary>
    public Task<GameClient> ConnectStorytellerAsync(Action<StorytellerViewDto>? onViewChanged = null) =>
        ConnectStorytellerToTableAsync(GameId, onViewChanged);

    /// <summary>起一条**没有 Join** 的裸连接：负向用例用它证明"未持票据的连接什么都做不了"。</summary>
    public async Task<HubConnection> ConnectAnonymousAsync()
    {
        var connection = CreateConnection();
        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// 连到**指定的桌**并以说书人身份加入：出示**这一桌房主账号**的会话（多桌 D-0024 / 归属 D-0027）。
    /// </summary>
    /// <remarks>
    /// 桌通过连接串的 <c>?gameId=</c> 声明——与浏览器端同一机制，所以这条用例测的是真实链路，
    /// 而不是测试专用的旁路；身份走的是真实的账号会话（<c>JoinStorytellerWithAccount</c>），
    /// 也不再需要从库里读任何凭据。
    /// </remarks>
    public async Task<GameClient> ConnectStorytellerToTableAsync(
        GameId gameId,
        Action<StorytellerViewDto>? onViewChanged = null)
    {
        var owner = OwnerOf(gameId);
        var connection = CreateConnection($"/hub/game?gameId={Uri.EscapeDataString(gameId.Value)}");
        if (onViewChanged is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onViewChanged);
        }

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>(
            "JoinStorytellerWithAccount",
            owner.AccountSession);
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>某张桌的房主账号（D-0027：进主持台只认它）。</summary>
    public FixtureAccount OwnerOf(GameId gameId) =>
        _owners.TryGetValue(gameId, out var owner)
            ? owner
            : throw new InvalidOperationException($"这张桌没有登记房主：{gameId.Value}（先用 RegisterTableAsync 建它）");

    /// <summary>
    /// 注册一个**夹具账号**：走真实的账号服务（注册即登录），返回它的账号会话。
    /// </summary>
    /// <remarks>
    /// 夹具只借这条路径造身份，不绕过任何产品判定；进主持台仍然要过真实的
    /// <c>JoinStorytellerWithAccount</c>（房主才进得去）。签发台本身在 <see cref="FixtureAccounts"/>。
    /// </remarks>
    public Task<FixtureAccount> RegisterAccountAsync(string username, string displayName, string password) =>
        Fixtures.RegisterAsync(username, displayName, password);

    /// <summary>
    /// 新建一张桌并把它记在一个**夹具账号**名下（D-0027：桌归属开桌账号）。
    /// </summary>
    /// <remarks>
    /// **重启场景**（同一个库、第二个宿主）里这张桌已经在册：那时不重新注册账号
    /// （登录名会撞车），而是给**原房主**重新签发一条会话——这正好也是真实用法里
    /// "换台设备登录同一账号回来"的那条路径。
    /// </remarks>
    public async Task<GameSetup> RegisterTableAsync(GameId gameId, int seatCount)
    {
        var catalog = _factory.Services.GetRequiredService<IGameCatalog>();
        if (await catalog.FindAsync(gameId, CancellationToken.None) is { CreatedByAccountId: { } ownerId } existing)
        {
            _owners[gameId] = await Fixtures.ReissueSessionAsync(ownerId);
            return existing;
        }

        var owner = await RegisterAccountAsync(
            $"fixture-owner-{gameId.Value}",
            $"房主-{gameId.Value}",
            FixtureAccounts.FixtureOwnerPassword);
        return await RegisterTableAsync(gameId, seatCount, owner);
    }

    /// <summary>
    /// 新建一张桌并把它记在 <paramref name="owner"/> 名下；返回它的会话信息。
    /// </summary>
    /// <remarks>
    /// 直接写会话目录（与开桌用例同一个 <see cref="GameSetupFactory"/> 形状），
    /// 因为夹具需要**指定桌标识**（真实开桌用例生成随机标识，测试要按标识寻址）。
    /// 归属仍然如实写在 <see cref="GameSetup.CreatedByAccountId"/> 上，进主持台也仍然过真实判定。
    /// </remarks>
    public async Task<GameSetup> RegisterTableAsync(GameId gameId, int seatCount, FixtureAccount owner)
    {
        var setup = GameSetupFactory.Create(gameId, seatCount, owner.Id);
        await _factory.Services.GetRequiredService<IGameCatalog>().SaveAsync(setup, CancellationToken.None);
        await _factory.Services.GetRequiredService<GameRegistry>().GetOrCreateAsync(gameId, CancellationToken.None);
        _owners[gameId] = owner;
        return setup;
    }

    /// <summary>以宿主身份执行命令，并像 Server 一样把通知推出去（开阶段等宿主动作）。</summary>
    public async Task<CommandResult> ExecuteHostCommandAsync(
        GameCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await Session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = command,
                Actor = Actor.Host,
                IdempotencyKey = idempotencyKey,
            },
            cancellationToken);
        await _factory.Services.GetRequiredService<NotificationDispatcher>().DispatchAsync(Game, result, cancellationToken);
        return result;
    }

    /// <summary>以系统身份执行命令（测试里显式驱动钟盘收票到点；生产由节拍器发出）。</summary>
    public async Task<CommandResult> ExecuteSystemCommandAsync(
        GameCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await Session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = command,
                Actor = Actor.System,
                IdempotencyKey = idempotencyKey,
            },
            cancellationToken);
        await _factory.Services.GetRequiredService<NotificationDispatcher>().DispatchAsync(Game, result, cancellationToken);
        return result;
    }

    /// <summary>轮询等待一个条件成立（测试用，超时有界）。</summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>轮询说书人视图直到条件成立；超时返回最后一次视图。</summary>
    public static async Task<StorytellerViewDto?> WaitForViewAsync(
        GameClient storyteller,
        Func<StorytellerViewDto, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        StorytellerViewDto? view = null;
        while (DateTime.UtcNow < deadline)
        {
            view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (predicate(view))
            {
                return view;
            }

            await Task.Delay(25);
        }

        return view;
    }

    /// <summary>当前事件流的最后序号（给「等基线之后的新事件」的谓词做基线）。</summary>
    public static async Task<long> LastSequenceAsync(TestServerHost host)
    {
        var stored = await host.Store.ReadEventsAsync(GameId, 0, CancellationToken.None);
        return stored.Count == 0 ? 0 : stored.Max(item => item.Sequence);
    }

    /// <summary>
    /// 等到指定槽位的「配额到点」事件真正落库（基线序号之后的新事件）。回归里不能用 sleep 猜时序：
    /// 猜早了断言可能假绿，猜晚了又变成 flaky；等到事件即确定性。
    /// </summary>
    public static async Task WaitForSlotQuotaElapsedAsync(
        TestServerHost host,
        long afterSequence,
        string slotId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var stored = await host.Store.ReadEventsAsync(GameId, 0, CancellationToken.None);
            if (stored.Any(item => item.Sequence > afterSequence
                && item.Event is SlotQuotaElapsedEvent elapsed
                && elapsed.SlotId == new StepSlotId(slotId)))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"槽位 {slotId} 的「配额到点」事件没有在超时前落库（基线序号 {afterSequence}）");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _factory.DisposeAsync();

        // SQLite 连接池会继续持有临时库文件句柄：清池后再删文件，否则 teardown 会假红
        SqliteConnection.ClearAllPools();

        // **不能改成"最后一个使用者删"**（试过，19 条重启类用例当场变红）：重启类用例是
        // "第一个宿主 dispose → 第二个宿主起来"，两者并非同时在场，引用计数会在中间归零，
        // 于是第二个宿主起来时库已经没了。
        //
        // 代价是显式共享库路径的用例（`deleteDatabaseOnDispose: false`）会把临时库留在 %TEMP%，
        // 由收尾统一清理；见票据 `done/deployment-verification-cleanup.md` 的"测试卫生"一节。
        if (_deleteDatabaseOnDispose)
        {
            TestDatabaseFiles.Delete(_databasePath);
        }
    }

    /// <summary>宿主基地址（测试要自建"连到指定桌"的连接时用）。</summary>
    public Uri ServerBaseAddress => _factory.Server.BaseAddress;

    /// <summary>宿主的 HTTP 处理器（测试自建连接时挂上它，请求才走内存管线）。</summary>
    public HttpMessageHandler ServerHandler() => _factory.Server.CreateHandler();

    /// <summary>
    /// 建一条 Hub 连接。默认连到**默认桌**并显式声明 <c>?gameId=</c>——
    /// D-0027 之后不声明桌标识的连接一律被拒（默认桌回落已删除），夹具也走同一条规则。
    /// </summary>
    private HubConnection CreateConnection(string? path = null) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(
                    _factory.Server.BaseAddress,
                    path ?? $"/hub/game?gameId={Uri.EscapeDataString(GameId.Value)}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();
}
