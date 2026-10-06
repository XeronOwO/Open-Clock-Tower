using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 账号入口的失败计数与限速（M4 / G-A1-1）：**在跑慢哈希之前**就把爆破挡掉。
/// </summary>
/// <remarks>
/// <para>
/// 键是"客户端地址 + 登录名"两个桶，取更严的那个（口径见 <see cref="ThrottleOptions"/> 与 D-0032）。
/// 失败才计数、成功即清窗口，所以正常用户打错两次不会被拖慢；而每一次失败尝试都要跑满一次
/// PBKDF2（单次 ~55 ms），它的放大效应正是这条要拦的东西——拒绝发生在哈希之前。
/// </para>
/// <para>
/// 状态只活在进程内存里：重启即清零（与账号会话同一条口径，D-0029）。
/// 多实例部署时它是**每实例一份**的——本项目首版明确不做多实例（见根 AGENTS.md「首版明确不做」），
/// 真要做时这一层要换成共享存储，而不是假装它是全局的。
/// </para>
/// <para>
/// 内存有界：桶数超过 <see cref="ThrottleOptions.MaxTrackedBuckets"/> 先清过期桶，
/// 仍满则不再为新键建桶（既有键继续计数）——随机登录名的洪水打不爆内存。
/// </para>
/// </remarks>
public sealed class AccountAttemptLimiter
{
    private readonly ThrottleOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<AccountAttemptLimiter> _logger;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    /// <summary>构造限速器。</summary>
    public AccountAttemptLimiter(
        IOptions<ThrottleOptions> options,
        IClock clock,
        ILogger<AccountAttemptLimiter> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>当前跟踪的计数桶数量（测试与运维读数）。</summary>
    public int TrackedBuckets => _buckets.Count;

    /// <summary>窗口时长。</summary>
    private TimeSpan Window => TimeSpan.FromSeconds(_options.WindowSeconds);

    /// <summary>这一次尝试放不放行（只读，不改计数）。</summary>
    public ThrottleDecision Check(ThrottleAction action, string client, string? username)
    {
        var now = _clock.UtcNow;
        var allowed = true;
        var longestRemaining = 0.0;

        foreach (var key in KeysOf(action, client, username))
        {
            if (!_buckets.TryGetValue(key.Id, out var bucket))
            {
                continue;
            }

            var elapsed = now - bucket.WindowStart;
            if (elapsed >= Window || bucket.Count < key.Limit)
            {
                continue;
            }

            allowed = false;
            longestRemaining = Math.Max(longestRemaining, (Window - elapsed).TotalSeconds);
        }

        return allowed ? ThrottleDecision.Allow : ThrottleDecision.Deny(longestRemaining);
    }

    /// <summary>记一次尝试：登录 / 重置只在失败时调用它，注册**每次调用都算**（成功与失败都算）。</summary>
    public void RecordAttempt(ThrottleAction action, string client, string? username)
    {
        var now = _clock.UtcNow;
        foreach (var key in KeysOf(action, client, username))
        {
            Increment(key, now);
        }
    }

    /// <summary>
    /// 记一次成功：清掉相关桶的窗口，正常用户不背历史包袱。
    /// </summary>
    /// <remarks>
    /// **注册的额度不在这里清**——那正是要限制的东西（成功即清等于没有上限）。
    /// </remarks>
    public void RecordSuccess(ThrottleAction action, string client, string? username)
    {
        if (action == ThrottleAction.Register)
        {
            return;
        }

        foreach (var key in KeysOf(action, client, username))
        {
            _buckets.TryRemove(key.Id, out _);
        }
    }

    /// <summary>按入口给出本次要判的桶（键 + 该桶的额度）。</summary>
    private BucketKey[] KeysOf(ThrottleAction action, string client, string? username) => action switch
    {
        ThrottleAction.Login =>
        [
            new BucketKey($"login:user|{client}|{username}", _options.LoginFailuresPerUsername),
            new BucketKey($"login:client|{client}", _options.LoginFailuresPerClient),
        ],
        ThrottleAction.Register =>
        [
            new BucketKey($"register:client|{client}", _options.RegisterCallsPerClient),
        ],
        ThrottleAction.ResetPassword =>
        [
            new BucketKey($"reset:user|{client}|{username}", _options.ResetFailuresPerUsername),
        ],
        _ => [],
    };

    /// <summary>把某个桶的计数推进一格；窗口已过则重新开窗。</summary>
    private void Increment(BucketKey key, DateTimeOffset now)
    {
        if (_buckets.TryGetValue(key.Id, out var existing))
        {
            _buckets[key.Id] = Advance(existing, now);
            return;
        }

        if (_buckets.Count >= _options.MaxTrackedBuckets)
        {
            Prune(now);
            if (_buckets.Count >= _options.MaxTrackedBuckets)
            {
                // 到顶了：新键不建桶。同一客户端那个桶（只有一个键、必然还在）仍然是兜底。
                _logger.LogDebug(
                    "限速桶已达上限 {Limit}：新键 {BucketKey} 不再单独计数",
                    _options.MaxTrackedBuckets,
                    key.Id);
                return;
            }
        }

        _buckets.AddOrUpdate(key.Id, _ => new Bucket(1, now), (_, current) => Advance(current, now));
    }

    private Bucket Advance(Bucket bucket, DateTimeOffset now)
    {
        var elapsed = now - bucket.WindowStart;
        return elapsed >= Window ? new Bucket(1, now) : bucket with { Count = bucket.Count + 1 };
    }

    /// <summary>清掉所有过期桶。</summary>
    private void Prune(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var pair in _buckets)
        {
            if (now - pair.Value.WindowStart >= Window && _buckets.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogDebug(
                "限速桶清理：移除 {Removed} 个过期桶，剩余 {Remaining}",
                removed,
                _buckets.Count);
        }
    }

    /// <summary>一个计数桶：窗口起点 + 窗口内的次数。</summary>
    private sealed record Bucket(int Count, DateTimeOffset WindowStart);

    /// <summary>一个受额度约束的键。</summary>
    private readonly record struct BucketKey(string Id, int Limit);
}
