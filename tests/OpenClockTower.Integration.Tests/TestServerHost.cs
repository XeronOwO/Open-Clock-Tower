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
            builder.ConfigureLogging(logging => logging.AddProvider(new CollectingLoggerProvider(Logs)));
        });

        // 触发宿主启动：建库、恢复事件流、播种会话票据
        _ = _factory.Services;

        // 按需开启测试夹具夜晚：已有阶段（重启恢复等场景）不重开，保持与旧引导行为一致
        if (autoStartTestNight && Session.GetStorytellerView().Phase is null)
        {
            ExecuteHostCommandAsync(
                new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
                "test-bootstrap:test-night-1",
                CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    /// <summary>宿主服务容器。</summary>
    public IServiceProvider Services => _factory.Services;

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

    /// <summary>以某席位加入（可挂收件回调）；返回带凭据的客户端。带 <paramref name="accountSession"/> 时同时认领席位（D-0021）。</summary>
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
        var joined = accountSession is null
            ? await connection.InvokeCoreAsync<SeatJoinDto>("JoinSeat", [ticket, lastSequence])
            : await connection.InvokeCoreAsync<SeatJoinDto>(
                "JoinSeatWithAccount",
                [ticket, accountSession, lastSequence]);
        Bundles[seat] = joined.Bundle;
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>只凭账号加入（D-0021）：不带票据，服务端按绑定解出席位（"认领之后的重连"路径）。</summary>
    public async Task<GameClient> ConnectSeatByAccountAsync(string accountSession, long lastSequence = 0)
    {
        var connection = CreateConnection();
        await connection.StartAsync();
        var joined = await connection.InvokeCoreAsync<SeatJoinDto>(
            "JoinSeatWithAccount",
            [string.Empty, accountSession, lastSequence]);
        Bundles[new SeatId(joined.Bundle.View.Seat)] = joined.Bundle;
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

    /// <summary>以说书人身份加入；返回带凭据的客户端。</summary>
    public async Task<GameClient> ConnectStorytellerAsync(Action<StorytellerViewDto>? onViewChanged = null)
    {
        var setup = await GetSetupAsync();
        var connection = CreateConnection();
        if (onViewChanged is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onViewChanged);
        }

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>("JoinStoryteller", setup.StorytellerTicket);
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>起一条**没有 Join** 的裸连接：负向用例用它证明"未持票据的连接什么都做不了"。</summary>
    public async Task<HubConnection> ConnectAnonymousAsync()
    {
        var connection = CreateConnection();
        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// 连到**指定的桌**并以说书人身份加入（多桌，D-0024）。
    /// </summary>
    /// <remarks>
    /// 桌通过连接串的 <c>?gameId=</c> 声明——与浏览器端同一机制，所以这条用例测的是真实链路，
    /// 而不是测试专用的旁路。
    /// </remarks>
    public async Task<GameClient> ConnectStorytellerToTableAsync(
        GameId gameId,
        Action<StorytellerViewDto>? onViewChanged = null)
    {
        var setup = await _factory.Services.GetRequiredService<IGameCatalog>()
            .FindAsync(gameId, CancellationToken.None)
            ?? throw new InvalidOperationException($"要连接的桌不存在：{gameId.Value}");

        var connection = CreateConnection($"/hub/game?gameId={Uri.EscapeDataString(gameId.Value)}");
        if (onViewChanged is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onViewChanged);
        }

        await connection.StartAsync();
        var joined = await connection.InvokeAsync<StorytellerJoinDto>("JoinStoryteller", setup.StorytellerTicket);
        _connections.Add(connection);
        return new GameClient(connection, joined.Credential);
    }

    /// <summary>新建一张桌（写会话目录 + 让注册表装载它）；返回它的会话信息。</summary>
    public async Task<GameSetup> RegisterTableAsync(GameId gameId, int seatCount)
    {
        var setup = GameSetupFactory.Create(gameId, seatCount);
        await _factory.Services.GetRequiredService<IGameCatalog>().SaveAsync(setup, CancellationToken.None);
        await _factory.Services.GetRequiredService<GameRegistry>().GetOrCreateAsync(gameId, CancellationToken.None);
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

        if (_deleteDatabaseOnDispose)
        {
            DeleteIfExists(_databasePath);
            DeleteIfExists(_databasePath + "-wal");
            DeleteIfExists(_databasePath + "-shm");
        }
    }

    /// <summary>宿主基地址（测试要自建"连到指定桌"的连接时用）。</summary>
    public Uri ServerBaseAddress => _factory.Server.BaseAddress;

    /// <summary>宿主的 HTTP 处理器（测试自建连接时挂上它，请求才走内存管线）。</summary>
    public HttpMessageHandler ServerHandler() => _factory.Server.CreateHandler();

    private HubConnection CreateConnection(string path = "/hub/game") =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(20);
            }
        }
    }
}
