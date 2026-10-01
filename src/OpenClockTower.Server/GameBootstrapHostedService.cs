using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 启动引导：建库 → 恢复事件流 → 播种会话票据。
/// </summary>
/// <remarks>
/// <para>
/// 夜晚计划**不在引导阶段自动构建**：真实的《梦殒春宵》夜晚顺序表在 <c>OpenClockTower.Rules</c>，
/// 按它建表需要角色分配与角色行动契约，属结算引擎（docs/backlog/todo/settlement-engine.md）。
/// 在那之前，开阶段是宿主 / 说书人的显式动作——引导阶段不伪造计划。
/// </para>
/// <para>
/// 恢复失败时**不自动继续**：记录 Critical 并停在空状态，等说书人 / 宿主显式重建或开新阶段
/// （D-0014 能力 3：重建失败显式报错、不静默继续）。
/// </para>
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
            setup = GameSetupFactory.Create(_gameId, _options.SeatCount);
            await _catalog.SaveAsync(setup, cancellationToken);
            _logger.LogWarning(
                "已创建单局（会话票据仍是占位实现）：game={GameId} 席位={SeatCount} 说书人票据={StorytellerTicket} 票据={Tickets}",
                _gameId,
                setup.Seats.Count,
                setup.StorytellerTicket,
                string.Join(",", setup.Seats.Select(seat => $"{seat.Seat.Value}:{seat.Ticket}")));
        }

        if (restored && _session.GetStorytellerView().Phase is null)
        {
            _logger.LogInformation(
                "未自动开启夜晚阶段：等待宿主 / 说书人显式开阶段（顺序表数据在 OpenClockTower.Rules，建表属结算引擎）");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
