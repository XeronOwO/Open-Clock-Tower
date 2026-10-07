using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// **席位这一层的编排**（D-0012 / D-0021 / D-0038）：加入 / 重连的席位定位与账号认领、
/// 说书人对席位的两个动作（解绑、签发邀请码）。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）：这里**不碰 SignalR**（不写 Clients、不重投请求），
/// 只做"身份 → 席位 → 凭据 + 快照"的编排；推送与请求重投仍由 Hub 完成。
/// </para>
/// <para>
/// **两条路径**（D-0037）：①凭邀请码 + 账号会话 → 核验邀请码定位席位并认领（邀请制桌与旅行者的唯一入口）；
/// ②只带账号 → 按绑定解出席位（认领之后的重连）。"没有账号、只凭票据入座"那条路整个删除：
/// 入座必须登录，账号会话从此**不是可空参数**。客户端声明一律不认（D-0012）：
/// 邀请码与会话只用于**定位**，授权仍由服务端签发的凭据链判定。
/// </para>
/// <para>
/// 邀请码从"住在席位上的明文票据"改成**单独核验的凭据**（D-0038 / 审计 G-A2-2）：
/// 它与席位名单不是一回事，因此这里多一道"核验出来的席位得真在名单里"的显式判定。
/// </para>
/// </remarks>
public sealed class SeatJoinCoordinator
{
    private readonly IGameCatalog _catalog;
    private readonly ConnectionRegistry _registry;
    private readonly SeatBindingService _bindings;
    private readonly AccountService _accounts;
    private readonly AccountSessionRegistry _sessions;
    private readonly SeatInvitationService _invitations;
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
        SeatInvitationService invitations,
        ILogger<SeatJoinCoordinator> logger)
    {
        _catalog = catalog;
        _registry = registry;
        _bindings = bindings;
        _accounts = accounts;
        _sessions = sessions;
        _invitations = invitations;
        _logger = logger;
    }

    /// <summary>执行一次加入：定位席位、按需认领、签发凭据并取回重连包。</summary>
    /// <param name="game">本次加入落在哪一桌（多桌，D-0024）。</param>
    /// <param name="ticket">邀请码（`桌标识:` 之后那一段）；空 = 只凭账号回到已认领席位。</param>
    /// <param name="accountSession">账号会话（必须；入座必须登录，D-0037）。</param>
    /// <param name="lastSequence">客户端已见序号。</param>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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
        var session = ResolveAccountSession(accountSession, connectionId);
        var accountId = session.Account;
        var seat = await ResolveSeatAsync(game, setup, ticket, accountId, connectionId, cancellationToken);
        RecordDepartedSeat(game, seat, connectionId, _logger);
        var claimed = await ClaimSeatAsync(game, seat, accountId, connectionId, cancellationToken);

        return await CompleteJoinAsync(game, seat, session, claimed, lastSequence, connectionId, cancellationToken);
    }

    /// <summary>
    /// **自助入座**（D-0025 / D-0037）：凭账号选一个空席位坐下，不需要任何票据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 四条前置判定，缺一不可：席位号在本桌范围内、席位未被**别人**占用、桌不在邀请制、
    /// 桌**没有开局**。第三条按账号判定——自己选自己已经坐着的席位是"回到座位"，不是抢占；
    /// 同理，**本人已认领的那一席在邀请制 / 已开局的桌上照样回得去**（刷新回座不能被开局吃掉）。
    /// </para>
    /// <para>
    /// "已开局"这一条此前只在前端拦（开局后不显示座位按钮），服务端从不判——那是"前端不显示按钮
    /// 不算鉴权"的反面案例（审计 G-A4-2 的第二半）。本批把它补在服务端，口径与大厅的置灰规则同源。
    /// </para>
    /// </remarks>
    /// <param name="game">要坐下的那一桌。</param>
    /// <param name="accountSession">账号会话（必须——入座必须登录，D-0037）。</param>
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
        var session = ResolveAccountSession(accountSession, connectionId);
        var accountId = session.Account;

        var seatId = new SeatId(seat);
        if (!setup.Seats.Contains(seatId))
        {
            _logger.LogWarning(
                "自助入座被拒（席位越界）：game={GameId} seat={Seat} 本桌席位={Capacity}",
                game.GameId.Value,
                seat,
                setup.Seats.Count);
            throw new HubException($"这一桌没有 {seat} 号席位（共 {setup.Seats.Count} 席）");
        }

        RecordDepartedSeat(game, seatId, connectionId, _logger);

        // 「回到我的座位」是**始终放行**的一格：邀请制与开局都只挡"新的入座"，不挡本人那一席。
        // 判据只有一条——这个账号是不是已经认领了它（与大厅的 `mySeatNumbers` 同源）。
        var binding = await _bindings.ResolveSeatAsync(game.GameId, accountId, cancellationToken);
        if (binding?.Seat != seatId)
        {
            if (setup.IsInviteOnly)
            {
                _logger.LogWarning(
                    "自助入座被拒（邀请制桌）：game={GameId} seat={Seat} account={AccountId} connection={ConnectionId}",
                    game.GameId.Value,
                    seat,
                    accountId.Value,
                    connectionId);
                throw new HubException("这一桌是邀请制：请向说书人要一个邀请码");
            }

            if (game.Session.HasStarted)
            {
                _logger.LogWarning(
                    "自助入座被拒（已开局）：game={GameId} seat={Seat} account={AccountId} connection={ConnectionId}",
                    game.GameId.Value,
                    seat,
                    accountId.Value,
                    connectionId);
                throw new HubException("这一桌已经开局：迟到的人请向说书人要一个邀请码");
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

        return await CompleteJoinAsync(game, seatId, session, claimed, lastSequence, connectionId, cancellationToken);
    }

    /// <summary>两条加入路径的共同后半段：签发凭据 + 取重连包。</summary>
    /// <param name="session">授权这次入座的账号会话：凭据把它记在身上，撤销才打得着（M2 / G-A2-1）。</param>
    private async Task<SeatJoinOutcome> CompleteJoinAsync(
        GameInstance game,
        SeatId seat,
        AccountSessionRef session,
        bool claimed,
        long lastSequence,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var credential = _registry.IssueForSeat(game.GameId, seat, connectionId, session);
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
                session.Account.Value);

            return new SeatJoinOutcome
            {
                Seat = seat,
                AccountId = session.Account,
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
    /// **审计带桌、操作者与来源**（M4 / G-A5-10）：这条日志原先既没有 game 也没有人，
    /// 出事时无法回答"谁把谁的席位解除了"。
    /// </remarks>
    public async Task<bool> ReleaseBindingAsync(
        GameInstance game,
        SeatId seat,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var setup = await _catalog.FindAsync(game.GameId, cancellationToken);
        if (setup is null)
        {
            _logger.LogWarning(
                "解除绑定被拒（会话）：game={GameId} seat={Seat} 原因=本局还没有会话信息 客户端={Client}",
                game.GameId.Value,
                seat,
                caller.Client);
            throw new HubException("本局还没有会话信息");
        }

        if (!setup.Seats.Contains(seat))
        {
            _logger.LogWarning(
                "解除绑定被拒（席位不在名单）：game={GameId} seat={Seat} 席位={Capacity} 客户端={Client}",
                game.GameId.Value,
                seat,
                setup.Seats.Count,
                caller.Client);
            throw new HubException("席位不在本局名单里");
        }

        var released = await _bindings.ReleaseAsync(game.GameId, seat, cancellationToken);
        if (released)
        {
            game.SeatNames.Remove(seat);
        }

        _logger.LogInformation(
            "解除席位绑定：game={GameId} seat={Seat} 已解除={Released} 操作者账号={AccountId} 连接={ConnectionId} 客户端={Client}",
            game.GameId.Value,
            seat,
            released,
            setup.CreatedByAccountId?.Value,
            caller.ConnectionId,
            caller.Client);

        return released;
    }

    /// <summary>
    /// 为某个席位**签发（或轮换）邀请码**（D-0038 / 审计 G-A2-2）：明文只回这一次，库里只留哈希。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="ReleaseBindingAsync"/> 同族：都是说书人对**席位**做的会话级动作，
    /// 不是游戏命令、不产生事件、不进复盘。放在这里是因为"席位合不合法"只该有一处判据。
    /// </para>
    /// <para>
    /// **重复签发 = 轮换**：旧的那一枚当场失效（"码发错人了"因此有一个干净的收场）；
    /// **有效期**由 <see cref="SeatInvitationOptions"/> 定，到期即作废，不需要任何清理动作。
    /// </para>
    /// </remarks>
    /// <param name="game">哪一桌。</param>
    /// <param name="seat">为哪一个席位签发。</param>
    /// <param name="caller">发起这次调用的客户端地址与连接（M4 / G-A5-10：审计要能回答"谁从哪来签的"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IssuedSeatInvitation> IssueInvitationAsync(
        GameInstance game,
        SeatId seat,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var setup = await LoadSetupAsync(game, caller.ConnectionId, cancellationToken);
        if (!setup.Seats.Contains(seat))
        {
            // 席位不在名单里就签不出码：否则会造出一张指向不存在席位的通行证，
            // 而那张通行证的失败点落在**玩家**那一面（他拿到手才知道进不去）。
            _logger.LogWarning(
                "签发邀请码被拒（席位不在名单）：game={GameId} seat={Seat} 席位={Capacity}"
                + " 操作者账号={AccountId} 连接={ConnectionId} 客户端={Client}",
                game.GameId.Value,
                seat.Value,
                setup.Seats.Count,
                setup.CreatedByAccountId?.Value,
                caller.ConnectionId,
                caller.Client);
            throw new HubException($"这一桌没有 {seat.Value} 号席位（共 {setup.Seats.Count} 席）");
        }

        var issued = await _invitations.IssueAsync(game.GameId, seat, cancellationToken);
        _logger.LogInformation(
            "已为席位签发邀请码：game={GameId} seat={Seat} 到期={ExpiresAt:o} 操作者账号={AccountId}"
            + " 连接={ConnectionId} 客户端={Client}",
            game.GameId.Value,
            seat.Value,
            issued.ExpiresAt,
            setup.CreatedByAccountId?.Value,
            caller.ConnectionId,
            caller.Client);

        return issued;
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
    /// 账号会话凭据 → **会话引用**（D-0021 / M2 G-A2-1）：缺失或无效一律显式拒绝，不静默降级。
    /// 客户端声明一律不认——这里只信服务端自己签发的会话（D-0012）。
    /// </summary>
    /// <remarks>
    /// **不再是可空返回**（D-0037）：入座必须登录，所以"没带会话"与"会话无效"是同一类拒绝，
    /// 只是文案不同。返回会话引用而不是账号：连接凭据要记住"是哪条会话授权了这次入座"，
    /// 撤销才打得着——登出只该踢那一条会话建立的连接，不能牵连同账号在别的设备上的登录。
    /// </remarks>
    private AccountSessionRef ResolveAccountSession(string? accountSession, string connectionId)
    {
        if (string.IsNullOrEmpty(accountSession))
        {
            _logger.LogWarning(
                "加入被拒：没有账号会话（入座必须登录）connection={ConnectionId}",
                connectionId);
            throw new HubException("入座需要先登录账号");
        }

        if (_sessions.TryResolveSession(accountSession, out var session))
        {
            return session;
        }

        _logger.LogWarning(
            "加入被拒：账号会话无效或已过期 connection={ConnectionId} 会话指纹={Fingerprint}",
            connectionId,
            AccountSessionCredential.FingerprintOf(accountSession));
        throw new HubException("账号会话无效或已过期，请重新登录（D-0021）");
    }

    /// <summary>
    /// 已离场的席位：**仍然接受重连**（席位与票据保留，R-0044 第 6 条），只是账上没有角色与生命标记。
    /// </summary>
    /// <remarks>
    /// 这里刻意**不**拒绝离场席位：本批曾经想"顺手"把"离场席位不能再进"补成一条闸，但那条口径
    /// 与已经登记的 R-0044 第 6 条（离场保留席位与票据，重连 / 复盘语义不动）相冲突，
    /// 而且已有用例锁着它（<c>TravellerHostTests.RemoveTraveller_KeepsTicket_…</c>）。
    /// 离场者重连后本人视图里的 <c>Departed</c> 为 true，界面据此说"你已离场"——不需要靠拒绝入座表达。
    /// </remarks>
    private static void RecordDepartedSeat(GameInstance game, SeatId seat, string connectionId, ILogger logger)
    {
        if (game.Session.HasDeparted(seat))
        {
            logger.LogInformation(
                "已离场席位重连：game={GameId} seat={Seat} connection={ConnectionId}（席位与票据保留：本人视图会明说已离场）",
                game.GameId.Value,
                seat,
                connectionId);
        }
    }

    /// <summary>定位席位：邀请码优先；没有邀请码时按账号绑定解出（认领之后的"只凭账号重连"路径）。</summary>
    /// <remarks>
    /// 邀请码那一支是**核验**而不是查找（D-0038）：出示的那一串与服务端存的哈希做固定时间比较，
    /// 命中且未过期才给出它指向的席位。因此库里没有可用明文凭据，而"过期"与"轮换"都能当场生效。
    /// </remarks>
    private async Task<SeatId> ResolveSeatAsync(
        GameInstance game,
        GameSetup setup,
        string? ticket,
        AccountId accountId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!string.IsNullOrEmpty(ticket))
        {
            var redemption = await _invitations.RedeemAsync(game.GameId, ticket, cancellationToken);
            if (!redemption.Accepted || redemption.Seat is not { } invited)
            {
                _logger.LogWarning(
                    "加入被拒（邀请码）：connection={ConnectionId} 原因={Reason} 指纹={Fingerprint}",
                    connectionId,
                    redemption.Reason,
                    SecretToken.FingerprintOf(ticket));
                throw new HubException(InvitationRejectionMessage(redemption.Reason));
            }

            if (!setup.Seats.Contains(invited))
            {
                // 邀请还在、席位却不在名单里（席位被裁掉 / 畸形库）：这道闸从前由"票据就住在席位上"隐式保证，
                // 拆开之后必须显式判——否则会给一个不存在的席位建出绑定。
                _logger.LogWarning(
                    "加入被拒（邀请码指向的席位不在名单里）：game={GameId} seat={Seat} connection={ConnectionId}",
                    game.GameId.Value,
                    invited.Value,
                    connectionId);
                throw new HubException($"邀请码无效：{invited.Value} 号席位不在这一桌的名单里");
            }

            return invited;
        }

        var binding = await _bindings.ResolveSeatAsync(game.GameId, accountId, cancellationToken);
        if (binding is not null)
        {
            return binding.Seat;
        }

        _logger.LogWarning(
            "加入被拒：账号还没有认领席位 connection={ConnectionId} account={AccountId}",
            connectionId,
            accountId);
        throw new HubException("这个账号还没有认领席位：请用邀请码加入一次，或从大厅挑一个空席位");
    }

    /// <summary>
    /// 邀请码被拒时给玩家看的话（D-0038）：**过期**与**根本没有**分开说。
    /// </summary>
    /// <remarks>
    /// 分开是有用的、不泄露的：说这句话的人手里本来就拿着那枚码，"它过期了"只帮他做对下一件事
    /// （去找说书人再要一个，而不是怀疑自己粘错了）；对没有码的人来说两条文案都只是"进不去"。
    /// </remarks>
    private static string InvitationRejectionMessage(string reason) =>
        reason == SeatInvitationRedemption.ExpiredReason
            ? "邀请码已过期：请向说书人再要一个"
            : "邀请码无效：这一桌没有这枚邀请码";

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
