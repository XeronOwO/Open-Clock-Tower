using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 节拍器：按服务端时钟把"配额到点"翻译成系统命令（D-0013）。
/// </summary>
/// <remarks>
/// 接管模式下由 <see cref="GameSession.TickAsync"/> 自行短路，不产生任何自动推进（D-0014 能力 2）；
/// 客户端时钟与此无关（D-0013 §6）。
/// </remarks>
public sealed class StepPacerHostedService : BackgroundService
{
    private readonly GameSession _session;
    private readonly GameServerOptions _options;
    private readonly ILogger<StepPacerHostedService> _logger;

    /// <summary>构造节拍器。</summary>
    public StepPacerHostedService(
        GameSession session,
        IOptions<GameServerOptions> options,
        ILogger<StepPacerHostedService> logger)
    {
        _session = session;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PacerIntervalMilliseconds));
        _logger.LogInformation("节拍器已启动：间隔={IntervalMs}ms", _options.PacerIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await _session.TickAsync(stoppingToken);
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
}
