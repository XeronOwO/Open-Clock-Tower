using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// **桌务**（说书人在开局前后的动作）：切换访问模式（公开 / 邀请制）、解除席位绑定。
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

    /// <summary>
    /// 换访问模式（说书人）：邀请制桌不接受新的自助入座，必须凭邀请码（D-0037）。
    /// </summary>
    /// <param name="game">哪一桌。</param>
    /// <param name="inviteOnly">true = 邀请制；false = 公开桌。</param>
    /// <param name="caller">发起这次调用的客户端地址与连接（审计要能回答"谁从哪来改的"，M4 / G-A5-10）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// **切换即时生效并推给该桌所有连接**（说书人 + 在场玩家，不刷新不重连就变）：
    /// 只在真的改了的时候推——重复设同一个值推一次空包，等于给所有人发一条"什么都没发生"。
    /// </remarks>
    public async Task<bool> SetInviteOnlyAsync(
        GameInstance game,
        bool inviteOnly,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var changed = await _lobby.UpdateLobbyAsync(game.GameId, name: null, inviteOnly, caller, cancellationToken);
        if (changed)
        {
            await _dispatcher.PushTableAccessChangedAsync(game, inviteOnly, cancellationToken);
        }

        return inviteOnly;
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

    /// <summary>
    /// 为某个席位签发（或轮换）邀请码（D-0038）：明文只回这一次，库里只留哈希。
    /// </summary>
    /// <remarks>
    /// 编排在 <see cref="SeatJoinCoordinator.IssueInvitationAsync"/>（与解绑同族：都是说书人对席位的动作）。
    /// 不是游戏命令、不产生事件，因此也不推给任何人——它是说书人一个人的事。
    /// </remarks>
    public Task<IssuedSeatInvitation> IssueInvitationAsync(
        GameInstance game,
        SeatId seat,
        CallerContext caller,
        CancellationToken cancellationToken) =>
        _join.IssueInvitationAsync(game, seat, caller, cancellationToken);
}
