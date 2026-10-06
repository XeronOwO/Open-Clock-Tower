using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// **连接 ↔ 桌的绑定**：解析一次并记住，并按该桌给出命令执行器。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）。职责单一：连接与局的绑定关系；
/// Hub 只负责"翻译与推送"。
/// </para>
/// <para>
/// 状态放在这里而不是 Hub 上：SignalR 的 Hub **每次调用新建一个实例**，字段记不住东西；
/// 本类是单例，绑定与执行器都按连接 id 记住（一条连接只服务一桌）。
/// </para>
/// <para>
/// 解析规则（D-0027）：连接串里的 <c>?gameId=</c> 是**唯一**依据。此前缺省会回落到宿主配置的默认桌，
/// 那条回落随默认桌一起删除——它同时是"一张没有房主的桌"的来源；未声明桌标识的连接**显式拒绝**。
/// 未知的桌同样显式拒绝，不做"顺手建一个"。
/// </para>
/// </remarks>
public sealed class HubGameScope
{
    private readonly GameRegistry _games;
    private readonly NotificationDispatcher _dispatcher;
    private readonly Dictionary<string, GameInstance> _gamesByConnection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HubCommandExecutor> _executorsByConnection = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>构造绑定。</summary>
    /// <param name="games">局注册表。</param>
    /// <param name="dispatcher">推送分发（执行器要用）。</param>
    internal HubGameScope(GameRegistry games, NotificationDispatcher dispatcher)
    {
        _games = games;
        _dispatcher = dispatcher;
    }

    /// <summary>取这条连接所属的桌；首次调用时按查询串解析并记住。</summary>
    /// <param name="httpContext">本次连接的 HTTP 上下文（查询串里必须有 gameId）。</param>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task<GameInstance> GameAsync(
        HttpContext? httpContext,
        string connectionId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_gamesByConnection.TryGetValue(connectionId, out var cached))
            {
                return cached;
            }
        }

        var declared = httpContext?.Request.Query["gameId"].ToString();
        if (string.IsNullOrWhiteSpace(declared))
        {
            // 不再回落默认桌（D-0027）：不声明桌标识的连接没有归属的局，也就没有任何可执行的动作。
            throw new HubException("这条连接没有声明桌标识（?gameId=）");
        }

        var gameId = new GameId(declared.Trim());
        var game = await _games.FindAsync(gameId, cancellationToken)
            ?? throw new HubException($"这一桌不存在：{gameId.Value}");

        lock (_gate)
        {
            _gamesByConnection[connectionId] = game;
        }

        return game;
    }

    /// <summary>取这条连接的命令执行器（按桌构造一次并复用；执行器持有会话，不能跨桌复用）。</summary>
    internal Task<HubCommandExecutor> CommandsAsync(
        HttpContext? httpContext,
        string connectionId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_executorsByConnection.TryGetValue(connectionId, out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        return CreateExecutorAsync(httpContext, connectionId, cancellationToken);
    }

    /// <summary>连接断开时清掉本连接的绑定，避免长跑进程里字典无限增长。</summary>
    internal void Forget(string connectionId)
    {
        lock (_gate)
        {
            _gamesByConnection.Remove(connectionId);
            _executorsByConnection.Remove(connectionId);
        }
    }

    private async Task<HubCommandExecutor> CreateExecutorAsync(
        HttpContext? httpContext,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var game = await GameAsync(httpContext, connectionId, cancellationToken);
        var executor = new HubCommandExecutor(game, _dispatcher);

        lock (_gate)
        {
            // 并发首调时可能已经有人放进去：保留先到的那个（两者等价，都是为了同一桌）。
            if (_executorsByConnection.TryGetValue(connectionId, out var existing))
            {
                return existing;
            }

            _executorsByConnection[connectionId] = executor;
        }

        return executor;
    }
}
