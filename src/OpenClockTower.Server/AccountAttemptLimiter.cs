using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 账号入口的失败计数与限速（M4 / G-A1-1）：**在跑慢哈希之前**就把爆破挡掉。
/// </summary>
/// <remarks>
/// <para>
/// 键是"客户端地址 + 登录名"两个桶，取更严的那个（口径见 <see cref="ThrottleOptions"/> 与 D-0032）；
/// 注册另加一个**全局桶**（M4 第一刀，G-A5-2）：单来源的额度拦不住"换 IP 分布式注册"，
/// 全局额度是那条路的兜底。
/// </para>
/// <para>
/// 失败才计数、成功即清窗口，所以正常用户打错两次不会被拖慢；而每一次失败尝试都要跑满一次
/// PBKDF2（单次 ~55 ms），它的放大效应正是这条要拦的东西——拒绝发生在哈希之前。
/// </para>
/// <para>
/// 计数本身在 <see cref="WindowedCounters"/>（有界、近似、进程内）；本类只负责"哪些入口、
/// 哪些键、多少额度、什么时候清零"这套口径。
/// </para>
/// </remarks>
public sealed class AccountAttemptLimiter
{
    /// <summary>全局注册桶的键（日志与测试按它检索）。</summary>
    private const string GlobalRegisterKey = "register:global";

    private readonly ThrottleOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<AccountAttemptLimiter> _logger;
    private readonly WindowedCounters _counters;

    /// <summary>构造限速器。</summary>
    public AccountAttemptLimiter(
        IOptions<ThrottleOptions> options,
        IClock clock,
        ILogger<AccountAttemptLimiter> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _counters = new WindowedCounters(
            TimeSpan.FromSeconds(_options.WindowSeconds),
            _options.MaxTrackedBuckets,
            logger);
    }

    /// <summary>当前跟踪的计数桶数量（测试与运维读数）。</summary>
    public int TrackedBuckets => _counters.TrackedKeys;

    /// <summary>这一次尝试放不放行（只读，不改计数）。</summary>
    public ThrottleDecision Check(ThrottleAction action, string client, string? username)
    {
        var now = _clock.UtcNow;
        var allowed = true;
        var longestRemaining = 0.0;

        foreach (var key in KeysOf(action, client, username))
        {
            if (_counters.Count(key.Id, now) < key.Limit)
            {
                continue;
            }

            allowed = false;
            longestRemaining = Math.Max(longestRemaining, _counters.RemainingSeconds(key.Id, now));
        }

        return allowed ? ThrottleDecision.Allow : ThrottleDecision.Deny(longestRemaining);
    }

    /// <summary>记一次尝试：登录 / 重置只在失败时调用它，注册**每次调用都算**（成功与失败都算）。</summary>
    public void RecordAttempt(ThrottleAction action, string client, string? username)
    {
        var now = _clock.UtcNow;
        foreach (var key in KeysOf(action, client, username))
        {
            _counters.Increment(key.Id, now);
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
            _counters.Reset(key.Id);
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

            // 全局桶（G-A5-2）：单来源额度挡不住换 IP 的分布式注册，这一条是兜底。
            // 键不带来源——它按定义就是"整个部署"的额度（多实例部署时每实例一份，见 D-0033 代价）。
            new BucketKey(GlobalRegisterKey, _options.RegisterCallsGlobal),
        ],
        ThrottleAction.ResetPassword =>
        [
            new BucketKey($"reset:user|{client}|{username}", _options.ResetFailuresPerUsername),
        ],
        ThrottleAction.DeleteAccount =>
        [
            new BucketKey($"delete:user|{client}|{username}", _options.DeleteFailuresPerUsername),
        ],
        _ => [],
    };

    /// <summary>一个受额度约束的键。</summary>
    private readonly record struct BucketKey(string Id, int Limit);
}
