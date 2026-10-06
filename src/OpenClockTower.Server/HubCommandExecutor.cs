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

    /// <summary>构造执行器（绑定到某一桌：会话与推送范围都不能跨桌）。</summary>
    internal HubCommandExecutor(GameInstance game, NotificationDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(game);

        _game = game;
        _dispatcher = dispatcher;
    }

    /// <summary>执行一条命令：跑会话 → 分发推送 → 转 wire DTO。</summary>
    /// <param name="actor">命令发起者（由凭据推导，D-0012）。</param>
    /// <param name="command">应用层命令。</param>
    /// <param name="idempotencyKey">幂等键（同一键重复提交只生效一次）。</param>
    /// <param name="clientSequence">客户端已见序号（重连补齐用；0 = 不声明）。</param>
    /// <param name="cancellationToken">连接中止令牌。</param>
    internal async Task<CommandResultDto> ExecuteAsync(
        Actor actor,
        GameCommand command,
        string idempotencyKey,
        long clientSequence,
        CancellationToken cancellationToken)
    {
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
