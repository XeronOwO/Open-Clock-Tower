using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// 命令执行机制：把「身份 + 应用层命令 + 幂等键」装进信封送进会话，把推送分发掉，
/// 再把结果映射成 wire DTO。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁，与 <see cref="HubActorResolver"/> /
/// <see cref="SeatJoinCoordinator"/> 同款）：Hub 只留**命令面**（wire 参数 → 命令翻译、
/// 凭据 → 身份），执行与结果映射在这里——两件事的变更理由不同，混在一个类里会让 Hub 一路长到超限。
/// </para>
/// <para>
/// 无状态：只持有会话与推送分发器两个协作者，调用方每次传入取消令牌（Hub 的连接中止令牌）。
/// </para>
/// </remarks>
internal sealed class HubCommandExecutor
{
    private readonly GameInstance _game;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ActionThrottle _throttle;
    private readonly CallerContext _caller;

    /// <summary>构造执行器（绑定到某一桌：会话与推送范围都不能跨桌）。</summary>
    /// <param name="game">这一条连接所属的桌。</param>
    /// <param name="dispatcher">推送分发。</param>
    /// <param name="throttle">动作准入（写文本的频率额度，M4 / G-A5-8）。</param>
    /// <param name="caller">来源地址与连接（准入的键与审计都用它，按连接固定不变）。</param>
    internal HubCommandExecutor(
        GameInstance game,
        NotificationDispatcher dispatcher,
        ActionThrottle throttle,
        CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(game);

        _game = game;
        _dispatcher = dispatcher;
        _throttle = throttle;
        _caller = caller;
    }

    /// <summary>执行一条命令：跑会话 → 分发推送 → 转 wire DTO。</summary>
    /// <param name="actor">命令发起者（由凭据推导，D-0012）。</param>
    /// <param name="command">应用层命令。</param>
    /// <param name="idempotencyKey">幂等键（同一键重复提交只生效一次）。</param>
    /// <param name="clientSequence">客户端已见序号（重连补齐用；0 = 不声明）。</param>
    /// <param name="cancellationToken">连接中止令牌。</param>
    /// <exception cref="HubException">写文本超出窗口额度时抛出（准入在进会话之前）。</exception>
    internal async Task<CommandResultDto> ExecuteAsync(
        Actor actor,
        GameCommand command,
        string idempotencyKey,
        long clientSequence,
        CancellationToken cancellationToken)
    {
        // 准入在会话之前（M4 / G-A5-8）：被限速的命令一个字节都不落库、也不触达内核。
        _throttle.AdmitCommand(_game.GameId, actor, command, _caller);

        var result = await _game.Session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = command,
                Actor = actor,
                IdempotencyKey = idempotencyKey,
                ClientSequence = clientSequence,
            },
            cancellationToken);

        await _dispatcher.DispatchAsync(_game, result, cancellationToken);
        return ProjectionMapper.ToDto(result);
    }
}
