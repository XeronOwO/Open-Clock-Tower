using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// **桌务**（说书人在开局前后的动作）：锁桌 / 解锁、解除席位绑定。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）。职责单一：桌级别的管理动作。
/// 与游戏内命令的区别很重要——**这两件事都不是游戏命令**：不产生事件、不进复盘，
/// 它们是会话信息（D-0015 / D-0025）。
/// </para>
/// <para>
/// 单例：只持有无状态协作者；"在哪一桌""推给谁"都属于每次调用，按参数传入。
/// </para>
/// </remarks>
public sealed class HubTableAdmin
{
    private readonly LobbyService _lobby;
    private readonly SeatJoinCoordinator _join;
    private readonly NotificationDispatcher _dispatcher;

    /// <summary>构造桌务入口。</summary>
    public HubTableAdmin(LobbyService lobby, SeatJoinCoordinator join, NotificationDispatcher dispatcher)
    {
        _lobby = lobby;
        _join = join;
        _dispatcher = dispatcher;
    }

    /// <summary>锁桌 / 解锁：锁定后不再接受新的自助入座，已在座的玩家不受影响（D-0025）。</summary>
    /// <param name="game">哪一桌。</param>
    /// <param name="isLocked">新的锁定状态。</param>
    /// <param name="caller">发起这次调用的客户端地址与连接（审计要能回答"谁从哪来锁的桌"，M4 / G-A5-10）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> SetLockAsync(
        GameInstance game,
        bool isLocked,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        await _lobby.UpdateLobbyAsync(game.GameId, name: null, isLocked, caller, cancellationToken);
        return isLocked;
    }

    /// <summary>解除席位绑定（D-0021 误认领兜底）：清掉「席位 ↔ 账号」并把新名字推给本桌。</summary>
    public async Task<bool> ReleaseBindingAsync(
        GameInstance game,
        SeatId seat,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var released = await _join.ReleaseBindingAsync(game, seat, caller, cancellationToken);
        if (released)
        {
            // 只有真的解除了才推送：没动用不着惊动全桌。
            await _dispatcher.PushSeatNamesChangedAsync(game, cancellationToken);
        }

        return released;
    }
}
