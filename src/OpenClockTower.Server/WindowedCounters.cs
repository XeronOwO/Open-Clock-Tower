using System.Collections.Concurrent;

namespace OpenClockTower.Server;

/// <summary>
/// 进程内的**有界窗口计数器**（M4 / D-0033）：所有"每个窗口最多 N 次"的账都记在这里。
/// </summary>
/// <remarks>
/// <para>
/// 从账号入口限速里抽出来的公共零件（M4 第一刀）：账号入口（<see cref="AccountAttemptLimiter"/>）
/// 与游戏内动作（<see cref="ActionThrottle"/>）用的是同一套窗口语义——**计数是近似的、内存是有界的、
/// 状态只活在进程里**。这三条口径各写一遍必然漂移，所以只留一处实现。
/// </para>
/// <para>
/// **有界**：键数超过上限时先清过期键；仍满则**不再为新键开账**（既有键继续计数）。
/// 随机键的洪水打不爆内存，代价是"极端时刻新键不被单独计数"——调用方据此选一个必然存在的兜底键。
/// </para>
/// <para>
/// **近似**：读改写不是原子的，并发下允许少记一两笔。对"每 5 分钟 N 次"这个量级的策略没有实际影响；
/// 换成精确计数要加锁，代价大于收益（与 D-0032 同一条口径）。
/// </para>
/// </remarks>
public sealed class WindowedCounters
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly TimeSpan _window;
    private readonly int _maxTrackedKeys;
    private readonly ILogger _logger;

    /// <summary>构造计数器。</summary>
    /// <param name="window">窗口时长（一个键在这个时长内累计，过期自动从零开始）。</param>
    /// <param name="maxTrackedKeys">最多跟踪多少个键（内存上界）。</param>
    /// <param name="logger">日志（到顶与清理留痕，便于排查"为什么没拦住"）。</param>
    public WindowedCounters(TimeSpan window, int maxTrackedKeys, ILogger logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTrackedKeys);

        _window = window;
        _maxTrackedKeys = maxTrackedKeys;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>当前跟踪的键数量（测试与运维读数）。</summary>
    public int TrackedKeys => _counters.Count;

    /// <summary>这个键在当前窗口里的计数；没有键或窗口已过 = 0。</summary>
    public int Count(string key, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(key, out var counter))
        {
            return 0;
        }

        return now - counter.WindowStart >= _window ? 0 : counter.Count;
    }

    /// <summary>这个键的窗口还剩多少秒；没有键或窗口已过 = 0。</summary>
    public double RemainingSeconds(string key, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(key, out var counter))
        {
            return 0;
        }

        var remaining = _window - (now - counter.WindowStart);
        return remaining <= TimeSpan.Zero ? 0 : remaining.TotalSeconds;
    }

    /// <summary>把某个键的计数推进一格；窗口已过则重新开窗。</summary>
    public void Increment(string key, DateTimeOffset now)
    {
        if (_counters.TryGetValue(key, out var existing))
        {
            _counters[key] = Advance(existing, now);
            return;
        }

        if (_counters.Count >= _maxTrackedKeys)
        {
            PruneExpired(now);
            if (_counters.Count >= _maxTrackedKeys)
            {
                _logger.LogDebug(
                    "窗口计数键已达上限 {Limit}：新键 {CounterKey} 不再单独计数",
                    _maxTrackedKeys,
                    key);
                return;
            }
        }

        _counters.AddOrUpdate(key, _ => new Counter(1, now), (_, current) => Advance(current, now));
    }

    /// <summary>忘掉一个键（配额"成功即清"这类语义用它）。</summary>
    public void Reset(string key) => _counters.TryRemove(key, out _);

    /// <summary>清掉所有过期键，返回清掉几个（测试与运维读数）。</summary>
    public int PruneExpired(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var pair in _counters)
        {
            if (now - pair.Value.WindowStart >= _window && _counters.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogDebug("窗口计数清理：移除 {Removed} 个过期键，剩余 {Remaining}", removed, _counters.Count);
        }

        return removed;
    }

    private Counter Advance(Counter counter, DateTimeOffset now) =>
        now - counter.WindowStart >= _window ? new Counter(1, now) : counter with { Count = counter.Count + 1 };

    /// <summary>一个计数格：窗口起点 + 窗口内的次数。</summary>
    private sealed record Counter(int Count, DateTimeOffset WindowStart);
}
