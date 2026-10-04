using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 加入 / 重连的席位定位与账号认领（D-0012 / D-0021）：票据 → 席位、账号会话 → 认领 / 解出席位，
/// 签发连接凭据并取回重连包。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）：这里**不碰 SignalR**（不写 Clients、不重投请求），
/// 只做"身份 → 席位 → 凭据 + 快照"的编排；推送与请求重投仍由 Hub 完成。
/// </para>
/// <para>
/// 三条路径：①带票据 + 账号 → 票据定位席位并首次认领；②只带账号（票据为空）→ 按绑定解出席位；
/// ③只带票据 → 游客入座，没有玩家名。客户端声明一律不认（D-0012）：票据与会话只用于**定位**，
/// 授权仍由服务端签发的凭据链判定。
/// </para>
/// </remarks>
public sealed class SeatJoinCoordinator
{
    private readonly IGameCatalog _catalog;
    private readonly GameId _gameId;
    private readonly GameSession _session;
    private readonly ConnectionRegistry _registry;
    private readonly SeatBindingService _bindings;
    private readonly AccountService _accounts;
    private readonly AccountSessionRegistry _sessions;
    private readonly SeatNameDirectory _seatNames;
    private readonly ILogger<SeatJoinCoordinator> _logger;

    /// <summary>构造加入编排。</summary>
    public SeatJoinCoordinator(
        IGameCatalog catalog,
        GameId gameId,
        GameSession session,
        ConnectionRegistry registry,
        SeatBindingService bindings,
        AccountService accounts,
        AccountSessionRegistry sessions,
        SeatNameDirectory seatNames,
        ILogger<SeatJoinCoordinator> logger)
    {
        _catalog = catalog;
        _gameId = gameId;
        _session = session;
        _registry = registry;
        _bindings = bindings;
        _accounts = accounts;
        _sessions = sessions;
        _seatNames = seatNames;
        _logger = logger;
    }

    /// <summary>执行一次加入：定位席位、按需认领、签发凭据并取回重连包。</summary>
    public async Task<SeatJoinOutcome> JoinAsync(
        string ticket,
        string? accountSession,
        long lastSequence,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var setup = await LoadSetupAsync(connectionId, cancellationToken);
        var accountId = ResolveAccountSession(accountSession, connectionId);
        var seat = await ResolveSeatAsync(setup, ticket, accountId, connectionId, cancellationToken);
        var claimed = accountId is { } claimedBy
            && await ClaimSeatAsync(seat, claimedBy, connectionId, cancellationToken);

        var credential = _registry.IssueForSeat(seat, connectionId);
        _logger.LogInformation(
            "已签发连接凭据：seat={Seat} connection={ConnectionId} 指纹={Fingerprint}（重连需重新出示票据）",
            seat,
            connectionId,
            ConnectionCredential.FingerprintOf(credential.Value));

        try
        {
            var bundle = await _session.GetReconnectBundleAsync(seat, lastSequence, cancellationToken);
            _logger.LogInformation(
                "玩家已加入：seat={Seat} connection={ConnectionId} 快照序号={Sequence} 本地已知={KnownSequence} 重投请求={Redelivered} 账号={AccountId}",
                seat,
                connectionId,
                bundle.Sequence,
                lastSequence,
                bundle.View.PendingRequest is not null,
                accountId?.Value);

            return new SeatJoinOutcome
            {
                Seat = seat,
                AccountId = accountId,
                Credential = credential,
                Bundle = bundle,
                Claimed = claimed,
            };
        }
        catch (InvalidOperationException exception)
        {
            // 事件流不可读（恢复失败后的降级房间）：显式失败 + 审计，不让未处理异常抛穿 Hub；
            // 对玩家只说中性原因——"数据丢了"属于说书人视图（票据 room-health-degradation-flag 的边界）。
            _logger.LogError(
                exception,
                "玩家加入失败：房间事件流不可读（等说书人显式重建）：seat={Seat} connection={ConnectionId}",
                seat,
                connectionId);
            throw new HubException("加入暂时失败，请稍后重试或联系说书人");
        }
    }

    /// <summary>
    /// 说书人 / 宿主解除席位绑定（D-0021：误认领兜底）：清掉「席位 ↔ 账号」，席位回到无名状态。
    /// </summary>
    /// <remarks>
    /// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）：与加入 / 认领同属"身份 ↔ 席位"的编排；
    /// 推送仍由 Hub 完成（本类不碰 SignalR）。不是游戏命令、不产生事件：绑定是会话信息。
    /// </remarks>
    public async Task<bool> ReleaseBindingAsync(SeatId seat, CancellationToken cancellationToken)
    {
        var setup = await _catalog.FindAsync(_gameId, cancellationToken);
        if (setup is null)
        {
            _logger.LogWarning("解除绑定被拒（会话）：原因=本局还没有会话信息");
            throw new HubException("本局还没有会话信息");
        }

        if (!setup.Seats.Any(item => item.Seat == seat))
        {
            _logger.LogWarning("解除绑定被拒（席位不在名单）：seat={Seat}", seat);
            throw new HubException("席位不在本局名单里");
        }

        var released = await _bindings.ReleaseAsync(_gameId, seat, cancellationToken);
        if (released)
        {
            _seatNames.Remove(seat);
        }

        _logger.LogInformation("解除席位绑定：seat={Seat} 已解除={Released}", seat, released);
        return released;
    }

    private async Task<GameSetup> LoadSetupAsync(string connectionId, CancellationToken cancellationToken)
    {
        var setup = await _catalog.FindAsync(_gameId, cancellationToken);
        if (setup is null)
        {
            _logger.LogWarning(
                "命令被拒绝（会话）：connection={ConnectionId} 原因=本局还没有会话信息",
                connectionId);
            throw new HubException("本局还没有会话信息");
        }

        return setup;
    }

    /// <summary>
    /// 账号会话凭据 → 账号标识（D-0021）：没带（游客）返回 null；带了但无效显式拒绝，不静默降级。
    /// 客户端声明一律不认——这里只信服务端自己签发的会话（D-0012）。
    /// </summary>
    private AccountId? ResolveAccountSession(string? accountSession, string connectionId)
    {
        if (string.IsNullOrEmpty(accountSession))
        {
            return null;
        }

        if (_sessions.TryResolve(accountSession, out var accountId))
        {
            return accountId;
        }

        _logger.LogWarning(
            "加入被拒：账号会话无效或已过期 connection={ConnectionId} 会话指纹={Fingerprint}",
            connectionId,
            AccountSessionCredential.FingerprintOf(accountSession));
        throw new HubException("账号会话无效或已过期，请重新登录（D-0021）");
    }

    /// <summary>定位席位：票据优先；没有票据时按账号绑定解出（认领之后的"只凭账号重连"路径）。</summary>
    private async Task<SeatId> ResolveSeatAsync(
        GameSetup setup,
        string? ticket,
        AccountId? accountId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(ticket))
        {
            var seatTicket = setup.Seats.FirstOrDefault(
                item => string.Equals(item.Ticket, ticket, StringComparison.Ordinal));
            if (seatTicket is null)
            {
                _logger.LogWarning("加入被拒：席位票据无效 connection={ConnectionId}", connectionId);
                throw new HubException("会话票据无效");
            }

            return seatTicket.Seat;
        }

        if (accountId is { } account)
        {
            var binding = await _bindings.ResolveSeatAsync(_gameId, account, cancellationToken);
            if (binding is not null)
            {
                return binding.Seat;
            }

            _logger.LogWarning(
                "加入被拒：账号还没有认领席位 connection={ConnectionId} account={AccountId}",
                connectionId,
                account);
            throw new HubException("这个账号还没有认领席位：请带上会话票据加入一次");
        }

        _logger.LogWarning("加入被拒：既没有票据也没有账号会话 connection={ConnectionId}", connectionId);
        throw new HubException("缺少会话票据：请用票据加入");
    }

    /// <summary>认领席位并更新席位名读模型；返回 true = 本次**新建**了绑定（需要推送新名字）。</summary>
    private async Task<bool> ClaimSeatAsync(
        SeatId seat,
        AccountId accountId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(accountId, cancellationToken)
            ?? throw new HubException("账号会话无效或已过期，请重新登录（D-0021）");

        var claim = await _bindings.ClaimAsync(_gameId, seat, accountId, cancellationToken);
        if (!claim.Accepted)
        {
            _logger.LogWarning(
                "加入被拒（席位认领）：connection={ConnectionId} seat={Seat} account={AccountId} code={Code} 说明={Message}",
                connectionId,
                seat,
                accountId,
                claim.Code,
                claim.Message);
            throw new HubException(claim.Message);
        }

        _seatNames.Set(seat, accountId, account.DisplayName);
        if (claim.Created)
        {
            _logger.LogInformation(
                "席位已认领：seat={Seat} account={AccountId} 玩家名={DisplayName}",
                seat,
                accountId,
                account.DisplayName);
        }

        return claim.Created;
    }
}
