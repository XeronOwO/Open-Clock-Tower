using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 空闲桌清扫（M5 / G-A5-5 容量半边 · G-A6-5）：判定 → **真的删** → 从注册表摘掉。
/// </summary>
/// <remarks>
/// <para>
/// 三个触发点共用这一个实现：**定时清扫**（<see cref="TableRetirementHostedService"/>）、
/// **开桌前自愈**（<see cref="LobbyService"/> 撞上全局桌数上限时先扫一轮）、
/// **运维维护命令**（<c>retire-tables</c>，那个进程里没有本类——用同一条判定与同一个存储端口）。
/// 一处实现三个入口，口径不会分叉。
/// </para>
/// <para>
/// 顺序是**先删库、后摘注册表**：删库失败（磁盘满、库被独占）时那一桌仍在册、仍能用，
/// 下一轮再来；反过来先摘注册表再删，一旦删失败就会得到"库里有、装载进不来"的桌，只能靠重启恢复。
/// </para>
/// <para>
/// 删除与摘表之间有一个极窄的窗口（有人刚好在这一刻入座）：库层那侧由
/// <see cref="ITableRetirementStore.PurgeAsync"/> 的孤儿清理兜底，注册表这侧则由本类删除后立刻摘掉。
/// 需要这个窗口真的被撞上，得先有一次"清扫判定之后、删除之前"的入座——而"有在线连接就不动"
/// 已经把绝大多数情况挡在门外。
/// </para>
/// </remarks>
public sealed class TableRetirementService
{
    private readonly ITableRetirementStore _store;
    private readonly TableRetirementPolicy _policy;
    private readonly ConnectionRegistry _connections;
    private readonly ITableUnloader _unloader;
    private readonly IClock _clock;
    private readonly TableRetentionOptions _options;
    private readonly ILogger<TableRetirementService> _logger;

    /// <summary>构造清扫器。</summary>
    public TableRetirementService(
        ITableRetirementStore store,
        TableRetirementPolicy policy,
        ConnectionRegistry connections,
        ITableUnloader unloader,
        IClock clock,
        IOptions<TableRetentionOptions> options,
        ILogger<TableRetirementService> logger)
    {
        _store = store;
        _policy = policy;
        _connections = connections;
        _unloader = unloader;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>自动清扫是否启用（定时与自愈两个入口看它；人肉维护命令不看——那是显式动作）。</summary>
    public bool AutomaticSweepEnabled => _options.Enabled;

    /// <summary>
    /// 扫一轮。
    /// </summary>
    /// <param name="apply">true = 真的回收；false = 只判定（运维先看一眼用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<TableRetirementReport> SweepAsync(bool apply, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var activities = await _store.ListActivityAsync(cancellationToken);
        var outcomes = new List<TableRetirementOutcome>(activities.Count);

        foreach (var activity in activities)
        {
            var outcome = _policy.Decide(activity, now, _connections.IsOccupied(activity.GameId));
            if (apply && outcome.Verdict == TableRetirementVerdict.Retire)
            {
                var purged = await _store.PurgeAsync(activity.GameId, cancellationToken);
                _unloader.Unload(activity.GameId);
                outcome = outcome with { Purged = purged };

                // 回收是不可逆动作，逐张记 Warn：事后要能回答"哪一桌、什么时候、为什么被删了"。
                // 回收是不可逆动作，逐张记 Warn：事后要能回答"哪一桌、什么时候、为什么被删了"。
                // 空闲时长写在「原因」里（人话）；这里刻意不再单列一个 TimeSpan——同一件事两种写法只会让人对不上。
                _logger.LogWarning(
                    "空闲桌已回收：game={GameId} 桌名={Name} 原因={Reason} "
                    + "删除行数=事件{Events}/快照{Snapshots}/回执{Receipts}/绑定{Bindings}/目录{Games} 孤儿={Orphans}"
                    + "（备份保留期内可恢复）",
                    activity.GameId.Value,
                    LogText.Clamp(activity.Name.Length == 0 ? "(未命名)" : activity.Name),
                    outcome.Reason,
                    purged.Events,
                    purged.Snapshots,
                    purged.Receipts,
                    purged.SeatBindings,
                    purged.Games,
                    purged.OrphanRows);
            }

            outcomes.Add(outcome);
        }

        var report = new TableRetirementReport(apply, now, outcomes);

        // 一轮一行（默认每小时）：这条是"回收到底有没有在跑"的唯一运行期读数，静默不可接受。
        _logger.LogInformation(
            "空闲桌清扫：在册={Total} · 到期={Due} · 已回收={Retired} · 在线跳过={InUse} · 无法判定={Undecidable}"
            + " · 保留期=未开局{EmptyHours}小时/已开局{PlayedDays}天 · {Mode}",
            outcomes.Count,
            report.DueCount,
            report.RetiredCount,
            report.InUseCount,
            report.UndecidableCount,
            _options.EmptyTableHours,
            _options.PlayedTableDays,
            apply ? "已执行" : "只报告");

        return report;
    }
}
