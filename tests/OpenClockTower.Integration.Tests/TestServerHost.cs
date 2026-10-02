using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>本局编排器（测试用来从宿主侧开阶段）。</summary>
    public GameSession Session => _factory.Services.GetRequiredService<GameSession>();

    /// <summary>事件存储（测试用来读事件与审计）。</summary>
    public IGameStore Store => _factory.Services.GetRequiredService<IGameStore>();

    /// <summary>默认游戏标识。</summary>
    public static GameId GameId => new("default");

    /// <summary>最近一次加入得到的重连包。</summary>
    public ConcurrentDictionary<SeatId, ReconnectBundleDto> Bundles { get; } = new();

    /// <summary>读取会话票据。</summary>
    public async Task<GameSetup> GetSetupAsync() =>
        await _factory.Services.GetRequiredService<IGameCatalog>().FindAsync(GameId, CancellationToken.None)
        ?? throw new InvalidOperationException("测试宿主尚未播种会话票据");

    /// <summary>以某席位加入（可挂收件回调）。</summary>
    public async Task<HubConnection> ConnectSeatAsync(
        SeatId seat,
        Action<OperationRequestDto>? onRequest = null,
        Action<OperationRequestVoidedDto>? onVoided = null,
        long lastSequence = 0,
        Action<StorytellerViewDto>? onStorytellerView = null,
        Action<InformationResultDto>? onInformation = null,
        Action<OperationRequestAnsweredDto>? onAnswered = null,
        Action<PhaseStartedDto>? onPhaseStarted = null)
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

        if (onStorytellerView is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onStorytellerView);
        }

        await connection.StartAsync();
        var bundle = await connection.InvokeAsync<ReconnectBundleDto>("JoinSeat", ticket, lastSequence);
        Bundles[seat] = bundle;
        _connections.Add(connection);
        return connection;
    }

    /// <summary>以说书人身份加入。</summary>
    public async Task<HubConnection> ConnectStorytellerAsync(Action<StorytellerViewDto>? onViewChanged = null)
    {
        var setup = await GetSetupAsync();
        var connection = CreateConnection();
        if (onViewChanged is not null)
        {
            connection.On<StorytellerViewDto>("ReceiveStorytellerViewChanged", onViewChanged);
        }

        await connection.StartAsync();
        await connection.InvokeAsync<StorytellerViewDto>("JoinStoryteller", setup.StorytellerTicket);
        _connections.Add(connection);
        return connection;
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
        await _factory.Services.GetRequiredService<NotificationDispatcher>().DispatchAsync(result, cancellationToken);
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
        HubConnection storyteller,
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

    private HubConnection CreateConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hub/game"), options =>
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
