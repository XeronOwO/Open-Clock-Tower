using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 启动引导：建库 → 恢复事件流 → 播种会话票据 → （恢复成功时）开启演示阶段。
/// </summary>
/// <remarks>
/// 恢复失败时**不自动继续**：记录 Critical 并停在空状态，等说书人 / 宿主显式重建或开新阶段
/// （D-0014 能力 3：重建失败显式报错、不静默继续）。
/// </remarks>
public sealed class GameBootstrapHostedService : IHostedService
{
    private readonly IDbContextFactory<GameDbContext> _dbFactory;
    private readonly IGameCatalog _catalog;
    private readonly GameId _gameId;
    private readonly GameSession _session;
    private readonly GameServerOptions _options;
    private readonly ILogger<GameBootstrapHostedService> _logger;

    /// <summary>构造引导服务。</summary>
    public GameBootstrapHostedService(
        IDbContextFactory<GameDbContext> dbFactory,
        IGameCatalog catalog,
        GameId gameId,
        GameSession session,
        IOptions<GameServerOptions> options,
        ILogger<GameBootstrapHostedService> logger)
    {
        _dbFactory = dbFactory;
        _catalog = catalog;
        _gameId = gameId;
        _session = session;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        var restored = true;
        try
        {
            await _session.RestoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            restored = false;
            _logger.LogCritical(
                exception,
                "事件流 / 快照恢复失败：房间停在空状态，等待显式重建或开新阶段（禁止静默继续）");
        }

        var setup = await _catalog.FindAsync(_gameId, cancellationToken);
        if (setup is null)
        {
            setup = GameSetupFactory.Create(_gameId, _options.DemoSeatCount);
            await _catalog.SaveAsync(setup, cancellationToken);
            _logger.LogWarning(
                "已创建演示局（规则数据未接入前的占位）：game={GameId} 席位={SeatCount} 说书人票据={StorytellerTicket} 票据={Tickets}",
                _gameId,
                setup.Seats.Count,
                setup.StorytellerTicket,
                string.Join(",", setup.Seats.Select(seat => $"{seat.Seat.Value}:{seat.Ticket}")));
        }

        if (!restored || _session.GetStorytellerView().Phase is not null)
        {
            return;
        }

        await _session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = new StartPhaseCommand { Plan = DemoStepPlan.CreateFirstNight(_options.DemoSeatCount) },
                Actor = Actor.Host,
                IdempotencyKey = "bootstrap:demo-night-1",
            },
            cancellationToken);
        _logger.LogInformation("演示阶段已开启：plan={Plan}", "demo:night-1");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
