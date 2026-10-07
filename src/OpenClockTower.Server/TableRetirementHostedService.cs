namespace OpenClockTower.Server;

/// <summary>
/// 定时清扫：按 <see cref="TableRetentionOptions.SweepIntervalMinutes"/> 一轮一轮地回收空闲桌
/// （M5 / G-A5-5 容量半边）。
/// </summary>
/// <remarks>
/// <para>
/// **启动就先扫一轮**：停机期间到期的桌不该再等一个间隔（重新部署 / 重启之后容量正是最紧的时候）。
/// 注册顺序排在 <see cref="GameBootstrapHostedService"/> **之后**，所以第一轮开始时
/// "装载在册的桌"已经做完了——否则会拿一份还没装好的注册表去判定。
/// </para>
/// <para>
/// 一轮失败不终止宿主（与 <see cref="StepPacerHostedService"/> 同一条口径）：回收是后台整理工作，
/// 它出错不该让服务停摆；下一轮会重试。
/// </para>
/// </remarks>
public sealed class TableRetirementHostedService : BackgroundService
{
    private readonly TableRetirementService _retirement;
    private readonly TableRetentionOptions _options;
    private readonly ILogger<TableRetirementHostedService> _logger;

    /// <summary>构造定时清扫。</summary>
    public TableRetirementHostedService(
        TableRetirementService retirement,
        Microsoft.Extensions.Options.IOptions<TableRetentionOptions> options,
        ILogger<TableRetirementHostedService> logger)
    {
        _retirement = retirement;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_retirement.AutomaticSweepEnabled)
        {
            // 关掉之后数据只进不出：全局桌数上限重新变成硬顶（见 TableRetentionOptions）。
            _logger.LogWarning(
                "空闲桌回收已关闭（{Section}:Enabled=false）：没人玩的桌会一直留在库里，"
                + "全局桌数上限因此变成硬顶；要清理只能用维护命令 retire-tables --apply。",
                TableRetentionOptions.SectionName);
            return;
        }

        _logger.LogInformation(
            "空闲桌清扫已启动：间隔={IntervalMinutes}分钟 · 保留期=未开局{EmptyHours}小时/已开局{PlayedDays}天",
            _options.SweepIntervalMinutes,
            _options.EmptyTableHours,
            _options.PlayedTableDays);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.SweepIntervalMinutes)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _retirement.SweepAsync(apply: true, stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "空闲桌清扫失败（不终止宿主，下一轮重试）");

                // 出错之后仍然要等一个间隔：库坏了的话，紧循环重试只会把日志刷满。
                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
