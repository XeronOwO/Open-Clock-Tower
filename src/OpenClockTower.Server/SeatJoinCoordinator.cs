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
    private readonly ConnectionRegistry _registry;
    private readonly SeatBindingService _bindings;
    private readonly AccountService _accounts;
    private readonly AccountSessionRegistry _sessions;
    private readonly ILogger<SeatJoinCoordinator> _logger;

    /// <summary>构造加入编排。</summary>
    /// <remarks>
    /// 多桌（D-0024）：本类**不再持有"当前是哪一局"**——会话、标识与席位名读模型都由调用方
    /// 按次传入（<see cref="GameInstance"/>）。持有单例会让"拿甲桌票据进乙桌"成立，
    /// 也会把两桌的玩家名混在一份读模型里。
    /// </remarks>
    public SeatJoinCoordinator(
        IGameCatalog catalog,
        ConnectionRegistry registry,
        SeatBindingService bindings,
        AccountService accounts,
        AccountSessionRegistry sessions,
        ILogger<SeatJoinCoordinator> logger)
    {
        _catalog = catalog;
        _registry = registry;
        _bindings = bindings;
        _accounts = accounts;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>执行一次加入：定位席位、按需认领、签发凭据并取回重连包。</summary>
    /// <param name="game">本次加入落在哪一桌（多桌，D-0024）。</param>
    public async Task<SeatJoinOutcome> JoinAsync(
        GameInstance game,
        string ticket,
        string? accountSession,
        long lastSequence,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var setup = await LoadSetupAsync(game, connectionId, cancellationToken);
        var accountId = ResolveAccountSession(accountSession, connectionId);
        var seat = await ResolveSeatAsync(game, setup, ticket, accountId, connectionId, cancellationToken);
        var claimed = accountId is { } claimedBy
            && await ClaimSeatAsync(game, seat, claimedBy, connectionId, cancellationToken);

        return await CompleteJoinAsync(game, seat, accountId, claimed, lastSequence, connectionId, cancellationToken);
    }

    /// <summary>
    /// **自助入座**（D-0025）：凭账号选一个空席位坐下，不需要任何票据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三条前置判定，缺一不可：桌未锁定、席位号在本桌范围内、席位未被**别人**占用。
    /// 第三条按账号判定——自己选自己已经坐着的席位是"回到座位"，不是抢占。
    /// </para>
    /// <para>
    /// 说书人票据仍然存在，但它只用于**成为说书人**；玩家这一侧从此不需要等发票据。
    /// </para>
    /// </remarks>
    /// <param name="game">要坐下的那一桌。</param>
    /// <param name="accountSession">账号会话（必须；游客仍走票据路径）。</param>
    /// <param name="seat">要坐的席位号。</param>
    /// <param name="lastSequence">客户端已见序号（重连补齐用）。</param>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SeatJoinOutcome> JoinBySeatAsync(
        GameInstance game,
        string accountSession,
        int seat,
        long lastSequence,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var setup = await LoadSetupAsync(game, connectionId, cancellationToken);
        var accountId = ResolveAccountSession(accountSession, connectionId)
            ?? throw new HubException("自助入座需要先登录账号");

        var seatId = new SeatId(seat);
        if (!setup.Seats.Any(item => item.Seat == seatId))
        {
            _logger.LogWarning(
                "自助入座被拒（席位越界）：game={GameId} seat={Seat} 本桌席位={Capacity}",
                game.GameId.Value,
                seat,
                setup.Seats.Count);
            throw new HubException($"这一桌没有 {seat} 号席位（共 {setup.Seats.Count} 席）");
        }

        // 锁桌只挡**新的**入座：已经坐在这张椅子上的人（含断线回来）不受影响——
        // 否则"锁桌"会变成"把在座玩家挡在门外"，那不是它的语义（D-0025）。
        if (setup.IsLocked)
        {
            var binding = await _bindings.ResolveSeatAsync(game.GameId, accountId, cancellationToken);
            if (binding?.Seat != seatId)
            {
                _logger.LogWarning(
                    "自助入座被拒（桌已锁定）：game={GameId} seat={Seat} account={AccountId} connection={ConnectionId}",
                    game.GameId.Value,
                    seat,
                    accountId.Value,
                    connectionId);
                throw new HubException("这一桌已经锁定，暂时不能再入座");
            }
        }

        // 占用判定交给认领服务：它同时覆盖"席位被别人占了"与"这个账号已经坐在别处"，
        // 并落到存储的唯一索引上——这里不另写一份判据（两份判据必然分叉）。
        var claimed = await ClaimSeatAsync(game, seatId, accountId, connectionId, cancellationToken);

        _logger.LogInformation(
            "玩家自助入座：game={GameId} seat={Seat} account={AccountId} 新建认领={Claimed}",
            game.GameId.Value,
            seat,
            accountId.Value,
            claimed);

        return await CompleteJoinAsync(game, seatId, accountId, claimed, lastSequence, connectionId, cancellationToken);
    }

    /// <summary>两条加入路径的共同后半段：签发凭据 + 取重连包。</summary>
    private async Task<SeatJoinOutcome> CompleteJoinAsync(
        GameInstance game,
        SeatId seat,
        AccountId? accountId,
        bool claimed,
        long lastSequence,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var credential = _registry.IssueForSeat(game.GameId, seat, connectionId);
        _logger.LogInformation(
            "已签发连接凭据：game={GameId} seat={Seat} connection={ConnectionId} 指纹={Fingerprint}（重连需重新出示凭据）",
            game.GameId.Value,
            seat,
            connectionId,
            ConnectionCredential.FingerprintOf(credential.Value));

        try
        {
            var bundle = await game.Session.GetReconnectBundleAsync(seat, lastSequence, cancellationToken);
            _logger.LogInformation(
                "玩家已加入：game={GameId} seat={Seat} connection={ConnectionId} 快照序号={Sequence} 本地已知={KnownSequence} 重投请求={Redelivered} 账号={AccountId}",
                game.GameId.Value,
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
                "玩家加入失败：房间事件流不可读（等说书人显式重建）：game={GameId} seat={Seat} connection={ConnectionId}",
                game.GameId.Value,
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
    public async Task<bool> ReleaseBindingAsync(GameInstance game, SeatId seat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var setup = await _catalog.FindAsync(game.GameId, cancellationToken);
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

        var released = await _bindings.ReleaseAsync(game.GameId, seat, cancellationToken);
        if (released)
        {
            game.SeatNames.Remove(seat);
        }

        _logger.LogInformation("解除席位绑定：seat={Seat} 已解除={Released}", seat, released);
        return released;
    }

    private async Task<GameSetup> LoadSetupAsync(
        GameInstance game,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var setup = await _catalog.FindAsync(game.GameId, cancellationToken);
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
        GameInstance game,
        GameSetup setup,
        string? ticket,
        AccountId? accountId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

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
            var binding = await _bindings.ResolveSeatAsync(game.GameId, account, cancellationToken);
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
        GameInstance game,
        SeatId seat,
        AccountId accountId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var account = await _accounts.FindAsync(accountId, cancellationToken)
            ?? throw new HubException("账号会话无效或已过期，请重新登录（D-0021）");

        var claim = await _bindings.ClaimAsync(game.GameId, seat, accountId, cancellationToken);
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

        game.SeatNames.Set(seat, accountId, account.DisplayName);
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
