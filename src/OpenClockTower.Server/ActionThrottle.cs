using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// **动作准入**（M4 / G-A5-6 · G-A5-8 的频率半）：入座与写文本这两类动作的窗口额度，超了当场拒绝。
/// </summary>
/// <remarks>
/// <para>
/// 这是**准入**而不是"限速中间件"：调用点在命令执行与加入入口上，拒绝方式是
/// <see cref="HubException"/>（人话文案），与参数层拒绝同一条路——客户端照常展示"多久之后再试"，
/// 服务端照常留审计。
/// </para>
/// <para>
/// 计数口径：**准入即计数**（不看命令最终成不成）。理由与账号入口相反——那里只有"失败"值得计
/// （成功即清），而这里要拦的是**调用次数本身**：一次被内核拒绝的入座同样已经把事件流读了一遍。
/// </para>
/// <para>
/// 键与额度见 <see cref="ActionThrottleOptions"/>；入座判两个桶（本桌 + 本来源，取更严的那个），
/// 与账号入口"来源 + 目标"两个桶同款（D-0032）。
/// </para>
/// </remarks>
public sealed class ActionThrottle
{
    private readonly ActionThrottleOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<ActionThrottle> _logger;
    private readonly WindowedCounters _counters;

    /// <summary>构造动作准入。</summary>
    public ActionThrottle(
        IOptions<ActionThrottleOptions> options,
        IClock clock,
        ILogger<ActionThrottle> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _counters = new WindowedCounters(
            TimeSpan.FromSeconds(_options.WindowSeconds),
            _options.MaxTrackedKeys,
            logger);
    }

    /// <summary>当前跟踪的计数键数量（测试与运维读数）。</summary>
    public int TrackedKeys => _counters.TrackedKeys;

    /// <summary>
    /// 命令准入：**带自由文本的命令**才过这条闸（其余命令不受影响，见
    /// <see cref="ActionThrottleOptions"/> 的取舍说明）。
    /// </summary>
    /// <param name="gameId">哪一桌（额度按桌分开算，免得一桌刷满牵连另一桌）。</param>
    /// <param name="actor">谁发的（说书人 / 玩家席位各算一份）。</param>
    /// <param name="command">命令本体（按它判"是不是写文本"）。</param>
    /// <param name="caller">来源地址与连接（键与审计都用它）。</param>
    /// <exception cref="HubException">超出窗口额度时抛出（文案带"多久之后再试"）。</exception>
    public void AdmitCommand(GameId gameId, Actor actor, GameCommand command, CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!CommandTextLimits.CarriesFreeText(command))
        {
            return;
        }

        var key = $"writetext|{caller.Client}|{gameId.Value}|{ActorKey(actor)}";
        var decision = Admit(
            [new BucketKey(key, _options.WriteTextCallsPerActor)],
            $"写文本（注记 / 说明 / 原因）每个窗口最多 {_options.WriteTextCallsPerActor} 次",
            caller,
            $"game={gameId.Value} actor={ActorKey(actor)}");

        if (decision is { } rejection)
        {
            throw rejection;
        }
    }

    /// <summary>
    /// 入座 / 重连准入：每次加入都要读全量事件流重建重连包，所以它是"最便宜的 CPU 放大器"。
    /// </summary>
    /// <param name="gameId">要加入的那一桌。</param>
    /// <param name="caller">来源地址与连接。</param>
    /// <exception cref="HubException">超出窗口额度时抛出。</exception>
    public void AdmitJoin(GameId gameId, CallerContext caller)
    {
        var decision = Admit(
            [
                new BucketKey($"join:game|{caller.Client}|{gameId.Value}", _options.JoinCallsPerClientAndGame),
                new BucketKey($"join:client|{caller.Client}", _options.JoinCallsPerClient),
            ],
            $"加入 / 重连每个窗口最多 {_options.JoinCallsPerClientAndGame} 次（同一来源跨桌 {_options.JoinCallsPerClient} 次）",
            caller,
            $"game={gameId.Value}");

        if (decision is { } rejection)
        {
            throw rejection;
        }
    }

    /// <summary>判定 + 计数：全部桶都在额度内才放行，放行即记一次。</summary>
    private HubException? Admit(BucketKey[] keys, string what, CallerContext caller, string context)
    {
        var now = _clock.UtcNow;
        var allowed = true;
        var longestRemaining = 0.0;

        foreach (var key in keys)
        {
            if (_counters.Count(key.Id, now) < key.Limit)
            {
                continue;
            }

            allowed = false;
            longestRemaining = Math.Max(longestRemaining, _counters.RemainingSeconds(key.Id, now));
        }

        if (!allowed)
        {
            var decision = ThrottleDecision.Deny(longestRemaining);
            _logger.LogWarning(
                "动作被限速（{What}）：{Context} 客户端={Client} 连接={ConnectionId} 建议重试={RetryAfterSeconds}s",
                what,
                context,
                caller.Client,
                caller.ConnectionId,
                decision.RetryAfterSeconds);

            return new HubException($"{what}；请 {decision.RetryAfterSeconds} 秒后再试");
        }

        foreach (var key in keys)
        {
            _counters.Increment(key.Id, now);
        }

        return null;
    }

    /// <summary>身份键：说书人与每个席位各算一份（同一连接串了身份也分得开）。</summary>
    private static string ActorKey(Actor actor) =>
        actor.Seat is { } seat ? $"{actor.Kind}:{seat.Value}" : actor.Kind.ToString();

    /// <summary>一个受额度约束的键。</summary>
    private readonly record struct BucketKey(string Id, int Limit);
}
