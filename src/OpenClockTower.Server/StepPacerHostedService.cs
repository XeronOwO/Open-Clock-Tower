using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 节拍器：按服务端时钟把"配额到点"翻译成系统命令（D-0013）。
/// </summary>
/// <remarks>
/// <para>
/// 接管模式下由 <see cref="GameSession.TickAsync"/> 自行短路，不产生任何自动推进（D-0014 能力 2）；
/// 客户端时钟与此无关（D-0013 §6）。
/// </para>
/// <para>
/// **多桌（D-0024）**：一次心跳遍历**每一桌**（每桌各自推进自己的槽位、各自推送），
/// 而不是只推进某一局。某一桌的心跳抛错不影响其余桌——一格坏桌不该让别的桌停摆。
/// </para>
/// </remarks>
public sealed class StepPacerHostedService : BackgroundService
{
    private readonly GameRegistry _games;
    private readonly NotificationDispatcher _dispatcher;
    private readonly GameServerOptions _options;
    private readonly ILogger<StepPacerHostedService> _logger;

    /// <summary>构造节拍器。</summary>
    public StepPacerHostedService(
        GameRegistry games,
        NotificationDispatcher dispatcher,
        IOptions<GameServerOptions> options,
        ILogger<StepPacerHostedService> logger)
    {
        _games = games;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PacerIntervalMilliseconds));
        _logger.LogInformation("节拍器已启动：间隔={IntervalMs}ms（多桌：逐桌推进）", _options.PacerIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                foreach (var gameId in _games.GameIds)
                {
                    await TickAsync(gameId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "节拍器心跳失败（不终止宿主）");
            }
        }
    }

    /// <summary>推进一桌；该桌出错只记日志，不影响其余桌的推进。</summary>
    private async Task TickAsync(GameId gameId, CancellationToken stoppingToken)
    {
        try
        {
            var game = await _games.FindAsync(gameId, stoppingToken);
            if (game is null)
            {
                return;
            }

            var result = await game.Session.TickAsync(stoppingToken);
            if (result is not null)
            {
                // 心跳是"命令入口"之一：产生的操作请求 / 说书人视图变更必须照常推送，
                // 否则请求只会留在服务端（集成测试 AssignedDreamer_ReceivesRealOperationRequest 盯这条链路）。
                await _dispatcher.DispatchAsync(game, result, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "节拍器推进某一桌失败（不影响其余桌）：game={GameId}", gameId.Value);
        }
    }
}
