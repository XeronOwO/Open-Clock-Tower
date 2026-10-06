using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// **加入的入口**（玩家与说书人两侧）：从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）。
/// </summary>
/// <remarks>
/// <para>
/// 职责单一：把"这条连接 + 本次调用"拼成一次加入，按身份走对应的编排
/// （玩家 → <see cref="SeatJoinCoordinator"/>；说书人 → <see cref="HubJoinFlow"/>），
/// 并完成玩家侧特有的收尾（重投未响应请求、新认领时广播新名字）。
/// </para>
/// <para>
/// 做成单例：只持有无状态协作者；连接 id、取消令牌与"推给谁"都属于**每次调用**，按参数传入
/// （D-0012：凭据与连接一一对应，串了连接等于串了身份）。
/// </para>
/// </remarks>
public sealed class HubJoinScope
{
    private readonly IGameCatalog _catalog;
    private readonly HubGameScope _scope;
    private readonly SeatJoinCoordinator _join;
    private readonly ConnectionRegistry _registry;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<GameHub> _logger;

    /// <summary>构造入口。</summary>
    public HubJoinScope(
        IGameCatalog catalog,
        HubGameScope scope,
        SeatJoinCoordinator join,
        ConnectionRegistry registry,
        NotificationDispatcher dispatcher,
        ILogger<GameHub> logger)
    {
        _catalog = catalog;
        _scope = scope;
        _join = join;
        _registry = registry;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>玩家加入 / 重连（只凭票据，D-0012）。</summary>
    public Task<SeatJoinDto> JoinSeatAsync(
        IGameClient caller,
        HttpContext? httpContext,
        string connectionId,
        CancellationToken aborted,
        string ticket,
        long lastSequence) =>
        JoinSeatCoreAsync(caller, httpContext, connectionId, aborted, ticket, accountSession: null, lastSequence);

    /// <summary>玩家加入 / 重连（带账号会话：票据认领，或只凭账号回到已认领席位，D-0021）。</summary>
    public Task<SeatJoinDto> JoinSeatWithAccountAsync(
        IGameClient caller,
        HttpContext? httpContext,
        string connectionId,
        CancellationToken aborted,
        string ticket,
        string? accountSession,
        long lastSequence) =>
        JoinSeatCoreAsync(caller, httpContext, connectionId, aborted, ticket, accountSession, lastSequence);

    /// <summary>
    /// 玩家**自助入座**（D-0025）：登录后选一个空席位坐下，**不需要任何票据**。
    /// </summary>
    /// <remarks>
    /// 桌由本连接的 <c>?gameId=</c> 决定（与其余命令同源）。说书人票据仍然存在，
    /// 但它只用于"成为说书人"；玩家这一侧从此不必等发票据。
    /// </remarks>
    public async Task<SeatJoinDto> JoinTableAsync(
        IGameClient caller,
        HttpContext? httpContext,
        string connectionId,
        CancellationToken aborted,
        string accountSession,
        int seat,
        long lastSequence)
    {
        var game = await _scope.GameAsync(httpContext, connectionId, aborted);
        var outcome = await _join.JoinBySeatAsync(game, accountSession, seat, lastSequence, connectionId, aborted);
        return await CompleteJoinAsync(caller, game, outcome, aborted);
    }

    /// <summary>说书人加入：票据定位身份，签发连接凭据（同局同一时刻只保留一条有效说书人连接）。</summary>
    public async Task<StorytellerJoinDto> JoinStorytellerAsync(
        HttpContext? httpContext,
        string connectionId,
        CancellationToken aborted,
        string ticket)
    {
        var game = await _scope.GameAsync(httpContext, connectionId, aborted);
        var flow = new HubJoinFlow(_catalog, game, _registry, _logger, connectionId, aborted);
        return await flow.JoinStorytellerAsync(ticket);
    }

    private async Task<SeatJoinDto> JoinSeatCoreAsync(
        IGameClient caller,
        HttpContext? httpContext,
        string connectionId,
        CancellationToken aborted,
        string ticket,
        string? accountSession,
        long lastSequence)
    {
        var game = await _scope.GameAsync(httpContext, connectionId, aborted);
        var outcome = await _join.JoinAsync(game, ticket, accountSession, lastSequence, connectionId, aborted);
        return await CompleteJoinAsync(caller, game, outcome, aborted);
    }

    /// <summary>玩家加入的共同收尾：重投未响应请求 + 新认领时广播新名字 + 组装 DTO。</summary>
    private async Task<SeatJoinDto> CompleteJoinAsync(
        IGameClient caller,
        GameInstance game,
        SeatJoinOutcome outcome,
        CancellationToken aborted)
    {
        if (outcome.Bundle.View.PendingRequest is { } pending)
        {
            // 重投的请求状态属于这份快照：序号取快照序号，客户端合并时与快照同源。
            await caller.ReceiveOperationRequest(ProjectionMapper.ToDto(pending, outcome.Bundle.View.Sequence));
        }

        if (outcome.Claimed)
        {
            // 新认领：同桌其他在线席位要立刻看到新名字（本人这份重连包里已经带上了）。
            await _dispatcher.PushSeatNamesChangedAsync(game, aborted);
        }

        return new SeatJoinDto
        {
            Credential = outcome.Credential.Value,
            Bundle = ProjectionMapper.ToDto(outcome.Bundle),
        };
    }
}
