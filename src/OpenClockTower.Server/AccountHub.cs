using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// 账号自助 Hub（D-0021）：注册 / 登录 / 登出 / 改玩家名 / 恢复码重置。
/// </summary>
/// <remarks>
/// <para>
/// 账号操作**不是游戏命令**：不过四道闸、不碰游戏状态；它只签发 / 撤销账号会话凭据，
/// 并在改名后把新玩家名同步给本局已绑定席位（会话读模型 + 视图推送）。
/// </para>
/// <para>
/// 秘密纪律：口令与恢复码只进结果、绝不进日志；日志只写账号标识、登录名与凭据短指纹。
/// 账号会话是 bearer 凭据——谁出示谁就是这个账号，前端只存内存、不落盘（D-0012 §4.1）。
/// </para>
/// <para>
/// 限速（M4 / G-A1-1）：注册 / 登录 / 重置三个入口**先过 <see cref="AccountAttemptLimiter"/> 再做别的**，
/// 拒绝发生在慢哈希之前——每次失败尝试都要跑满一次 PBKDF2，不限速就是一台免费的 CPU 放大器。
/// 键是"客户端地址 + 登录名"，客户端地址来自 <see cref="ClientAddress"/>（反代头已在管线最前面归一）。
/// </para>
/// </remarks>
public sealed class AccountHub : Hub
{
    private readonly AccountService _accounts;
    private readonly AccountSessionRegistry _sessions;
    private readonly AccountRevocationService _revocation;
    private readonly AccountAttemptLimiter _limiter;
    private readonly GameRegistry _games;
    private readonly NotificationDispatcher _dispatcher;
    private readonly LobbyService _lobby;
    private readonly TableCreationPolicy _tableCreation;
    private readonly ILogger<AccountHub> _logger;

    /// <summary>构造账号 Hub。</summary>
    /// <remarks>
    /// <para>
    /// 多桌（D-0024）：改名要同步到**该账号所在的每一桌**，所以这里拿注册表逐桌更新，
    /// 而不是拿一份进程级的席位名读模型（那份已随单例一起删除）。
    /// 大厅（D-0025）也挂在这里：账号是入场凭据，大厅与账号自助是同一层的事。
    /// </para>
    /// <para>
    /// 撤销走 <see cref="AccountRevocationService"/>（M2 / G-A2-1）：登出与口令重置撤的不只是"下一次进门"，
    /// 还包括由那条会话建立、**已经进门**的连接。本类不直接调会话表的撤销，免得只做一半。
    /// </para>
    /// </remarks>
    public AccountHub(
        AccountService accounts,
        AccountSessionRegistry sessions,
        AccountRevocationService revocation,
        AccountAttemptLimiter limiter,
        GameRegistry games,
        NotificationDispatcher dispatcher,
        LobbyService lobby,
        TableCreationPolicy tableCreation,
        ILogger<AccountHub> logger)
    {
        _accounts = accounts;
        _sessions = sessions;
        _revocation = revocation;
        _limiter = limiter;
        _games = games;
        _dispatcher = dispatcher;
        _lobby = lobby;
        _tableCreation = tableCreation;
        _logger = logger;
    }

    /// <summary>这条连接的真实客户端地址（反代后面也能拿到真地址，见 <see cref="ClientAddress"/>）。</summary>
    private string Client => ClientAddress.Of(Context.GetHttpContext());

    /// <summary>
    /// 列出在开的桌（D-0025）：玩家挑桌用，登录即可看；带 <c>CreatedByMe</c> 供说书人面挑出"我的桌"。
    /// </summary>
    /// <param name="accountSession">账号会话（未登录传 null：大厅是公开门面，只是没有"我"）。</param>
    /// <remarks>
    /// 只返回公开信息（桌名 / 人数 / 是否开局 / 是否锁定 / 是不是你开的），不含票据与席位归属。
    /// 未登录也能看——真正入座与进主持台都要凭账号（D-0027）。
    /// </remarks>
    public async Task<IReadOnlyList<LobbyTableDto>> ListTables(string? accountSession)
    {
        var viewer = await ResolveAccountAsync(accountSession);
        return await _lobby.ListAsync(viewer?.Id, Context.ConnectionAborted);
    }

    /// <summary>
    /// 创建一张新桌（D-0026：**登录即可**；D-0027：开桌即成为这一桌的说书人）。
    /// </summary>
    /// <param name="accountSession">账号会话（开桌要记在某个账号头上，所以必须登录）。</param>
    /// <param name="name">桌名（可为空）。</param>
    /// <param name="seatCount">席位数。</param>
    /// <remarks>
    /// 授权在 <see cref="LobbyService.CreateAsync"/>（部署开关 + 运维名单），这里只解析账号：
    /// 未登录的拒绝与"本服不开放自助开桌"是两种结果码，前端要分开说。
    /// 回执里没有票据（D-0027）——前端拿到 <c>GameId</c> 就能进主持台，因为桌已经记在这个账号名下。
    /// </remarks>
    public async Task<LobbyCreateResultDto> CreateTable(string accountSession, string? name, int seatCount)
    {
        var account = await ResolveAccountAsync(accountSession);
        if (account is null)
        {
            return new LobbyCreateResultDto
            {
                Ok = false,
                Code = "invalid_session",
                Message = "账号会话无效或已过期，请重新登录",
            };
        }

        var result = await _lobby.CreateAsync(account, name, seatCount, Context.ConnectionAborted);
        if (result.Ok)
        {
            _logger.LogInformation(
                "已开桌：connection={ConnectionId} game={GameId} 开桌人={Username}",
                Context.ConnectionId,
                result.GameId,
                account.Username);
        }

        return result;
    }

    /// <summary>账号会话 → 账号（无效时返回 null；调用方给出中性拒绝）。</summary>
    private async Task<Account?> ResolveAccountAsync(string? accountSession)
    {
        if (string.IsNullOrEmpty(accountSession) || !_sessions.TryResolve(accountSession, out var accountId))
        {
            return null;
        }

        return await _accounts.FindAsync(accountId, Context.ConnectionAborted);
    }

    /// <summary>注册（D-0021）：成功后**同时登录**，返回账号会话与一次性恢复码。</summary>
    /// <remarks>注册的额度**每次调用都消耗**（成功与失败都算），且不因成功而清空——那正是要限制的东西（D-0032）。</remarks>
    public async Task<AccountDto> Register(string username, string displayName, string password)
    {
        var decision = _limiter.Check(ThrottleAction.Register, Client, username: null);
        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "注册被限速：connection={ConnectionId} 客户端={Client} 建议重试={RetryAfterSeconds}s",
                Context.ConnectionId,
                Client,
                decision.RetryAfterSeconds);
            return Throttled(decision);
        }

        _limiter.RecordAttempt(ThrottleAction.Register, Client, username: null);
        var outcome = await _accounts.RegisterAsync(username, displayName, password, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _logger.LogInformation(
                "注册被拒：connection={ConnectionId} 客户端={Client} code={Code} 原因={Message}",
                Context.ConnectionId,
                Client,
                outcome.Code,
                outcome.Message);
            return Reject(outcome);
        }

        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "账号已注册并登录：account={AccountId} username={Username} connection={ConnectionId} 客户端={Client} 会话指纹={Fingerprint}",
            outcome.Account.Id,
            outcome.Account.Username,
            Context.ConnectionId,
            Client,
            AccountSessionCredential.FingerprintOf(session.Value));
        return Accept(outcome.Account, session.Value, outcome.RecoveryCode);
    }

    /// <summary>登录：签发账号会话；失败一律中性文案（不暴露登录名是否存在）。</summary>
    /// <remarks>失败才计数，成功即清窗口：正常用户打错两次不会被拖慢（M4 / G-A1-1）。</remarks>
    public async Task<AccountDto> Login(string username, string password)
    {
        var usernameKey = UsernameKeyOf(username);
        var decision = _limiter.Check(ThrottleAction.Login, Client, usernameKey);
        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "登录被限速：connection={ConnectionId} 客户端={Client} 登录名={UsernameKey} 建议重试={RetryAfterSeconds}s",
                Context.ConnectionId,
                Client,
                usernameKey,
                decision.RetryAfterSeconds);
            return Throttled(decision);
        }

        var outcome = await _accounts.AuthenticateAsync(username, password, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _limiter.RecordAttempt(ThrottleAction.Login, Client, usernameKey);
            _logger.LogWarning(
                "登录被拒：connection={ConnectionId} 客户端={Client} 登录名={UsernameKey} code={Code}",
                Context.ConnectionId,
                Client,
                usernameKey,
                outcome.Code);
            return Reject(outcome);
        }

        _limiter.RecordSuccess(ThrottleAction.Login, Client, usernameKey);
        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "已登录：account={AccountId} username={Username} connection={ConnectionId} 客户端={Client} 会话指纹={Fingerprint}",
            outcome.Account.Id,
            outcome.Account.Username,
            Context.ConnectionId,
            Client,
            AccountSessionCredential.FingerprintOf(session.Value));
        return Accept(outcome.Account, session.Value, recoveryCode: null);
    }

    /// <summary>
    /// 用持久化的账号会话恢复登录态（M1 / D-0029）：只读、幂等，**不重发凭据、不续期**。
    /// </summary>
    /// <param name="accountSession">浏览器带回来的账号会话（`sessionStorage` 里那一份）。</param>
    /// <remarks>
    /// <para>
    /// 刷新页面之后前端拿它确认"这串凭据现在还算不算数"：有效就重建资料区（含能力位），
    /// 无效就清空持久化、退回登录卡——撤销性因此不退步（D-0029 口径 4）。
    /// </para>
    /// <para>
    /// 授权面：任何人凭**自己的**会话可调，回执只含自己的资料；本方法**不接受任何账号标识参数**，
    /// 因此不存在"查别人"的入口（登记进 M2 的"方法 × 允许身份"矩阵）。
    /// </para>
    /// </remarks>
    public async Task<AccountDto> Resume(string accountSession)
    {
        var account = await ResolveAccountAsync(accountSession);
        if (account is null)
        {
            _logger.LogWarning(
                "会话恢复被拒（无效或已过期）：connection={ConnectionId} 会话指纹={Fingerprint}",
                Context.ConnectionId,
                AccountSessionCredential.FingerprintOf(accountSession));
            return InvalidSession();
        }

        // 回执里不带凭据：恢复不是签发，明文会话不在这条路径上二次流转。
        _logger.LogDebug(
            "会话已恢复：account={AccountId} username={Username} connection={ConnectionId} 会话指纹={Fingerprint}",
            account.Id.Value,
            account.Username,
            Context.ConnectionId,
            AccountSessionCredential.FingerprintOf(accountSession));
        return Accept(account, accountSession: null, recoveryCode: null);
    }

    /// <summary>
    /// 登出：撤销这条账号会话**以及由它建立的在线连接**（幂等：已失效也返回成功，只是说明不同）。
    /// </summary>
    /// <remarks>
    /// M2 / G-A2-1：撤销编排在 <see cref="AccountRevocationService"/>——只撤会话等于让已经进门的
    /// 席位 / 主持台一直有效到刷新为止，那不是"登出"该有的覆盖面。
    /// </remarks>
    public Task<AccountDto> Logout(string accountSession)
    {
        var revoked = _revocation.RevokeSession(accountSession);
        _logger.LogInformation(
            "登出：connection={ConnectionId} 已撤销={Revoked} 会话指纹={Fingerprint}",
            Context.ConnectionId,
            revoked,
            AccountSessionCredential.FingerprintOf(accountSession));
        return Task.FromResult(new AccountDto
        {
            Ok = true,
            Code = revoked ? "ok" : "already_signed_out",
            Message = revoked ? "已登出" : "会话已失效，无需再次登出",
        });
    }

    /// <summary>改玩家名（D-0021）：账号设置里随时可改；改名即时同步给本局已绑定席位。</summary>
    public async Task<AccountDto> ChangeDisplayName(string accountSession, string displayName)
    {
        if (!_sessions.TryResolve(accountSession, out var accountId))
        {
            _logger.LogWarning(
                "改玩家名被拒（会话无效）：connection={ConnectionId} 会话指纹={Fingerprint}",
                Context.ConnectionId,
                AccountSessionCredential.FingerprintOf(accountSession));
            return InvalidSession();
        }

        var outcome = await _accounts.ChangeDisplayNameAsync(accountId, displayName, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _logger.LogInformation(
                "改玩家名被拒：account={AccountId} code={Code} 原因={Message}",
                accountId,
                outcome.Code,
                outcome.Message);
            return Reject(outcome);
        }

        // 名字是"读时解析"（D-0021）：逐桌更新该账号的席位名，并把变更推给**那些桌**。
        var updated = 0;
        foreach (var gameId in _games.GameIds)
        {
            var game = await _games.FindAsync(gameId, Context.ConnectionAborted);
            if (game is null)
            {
                continue;
            }

            game.SeatNames.Rename(accountId, outcome.Account.DisplayName);
            await _dispatcher.PushSeatNamesChangedAsync(game, Context.ConnectionAborted);
            updated++;
        }

        _logger.LogInformation(
            "玩家名已更新：account={AccountId} 新玩家名={DisplayName} 已同步的桌数={Tables}",
            accountId,
            outcome.Account.DisplayName,
            updated);
        return Accept(outcome.Account, accountSession: null, recoveryCode: null);
    }

    /// <summary>
    /// 恢复码重置口令（D-0021）：成功后撤销该账号全部旧会话、轮换恢复码，并直接重新登录。
    /// </summary>
    /// <remarks>
    /// M2 / G-A2-1：撤销面含**已经进门的连接**（<see cref="AccountRevocationService"/>）——
    /// "我怀疑账号泄露，改口令"必须当场把旧连接全部踢下线，否则改口令只是挡住了下一次登录。
    /// </remarks>
    public async Task<AccountDto> ResetPassword(string username, string recoveryCode, string newPassword)
    {
        var usernameKey = UsernameKeyOf(username);
        var decision = _limiter.Check(ThrottleAction.ResetPassword, Client, usernameKey);
        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "口令重置被限速：connection={ConnectionId} 客户端={Client} 登录名={UsernameKey} 建议重试={RetryAfterSeconds}s",
                Context.ConnectionId,
                Client,
                usernameKey,
                decision.RetryAfterSeconds);
            return Throttled(decision);
        }

        var outcome = await _accounts.ResetPasswordAsync(username, recoveryCode, newPassword, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _limiter.RecordAttempt(ThrottleAction.ResetPassword, Client, usernameKey);
            _logger.LogWarning(
                "口令重置被拒：connection={ConnectionId} 客户端={Client} 登录名={UsernameKey} code={Code}",
                Context.ConnectionId,
                Client,
                usernameKey,
                outcome.Code);
            return Reject(outcome);
        }

        _limiter.RecordSuccess(ThrottleAction.ResetPassword, Client, usernameKey);
        var revoked = _revocation.RevokeAllForAccount(outcome.Account.Id);
        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "口令已重置：account={AccountId} username={Username} 客户端={Client} 已撤销旧会话={Revoked} 已踢旧连接并重新登录",
            outcome.Account.Id,
            outcome.Account.Username,
            Client,
            revoked);
        return Accept(outcome.Account, session.Value, outcome.RecoveryCode);
    }

    /// <summary>限速用的登录名键：能归一就用归一后的（有界、大小写同一把尺），不能归一就用空串（只按客户端计数）。</summary>
    private static string UsernameKeyOf(string? username) =>
        UsernameText.TryNormalize(username, out var normalized, out _)
            ? UsernameText.ComparisonKeyOf(normalized)
            : string.Empty;

    /// <summary>限速拒绝：专用结果码 + 人话（多久之后再试），不是异常——前端要能平静地展示它。</summary>
    private static AccountDto Throttled(ThrottleDecision decision) => new()
    {
        Ok = false,
        Code = "too_many_attempts",
        Message = $"尝试过于频繁，请 {decision.RetryAfterSeconds} 秒后再试",
    };

    private static AccountDto Reject(AccountOutcome outcome) => new()
    {
        Ok = false,
        Code = outcome.Code,
        Message = outcome.Message,
    };

    private static AccountDto InvalidSession() => new()
    {
        Ok = false,
        Code = "invalid_session",
        Message = "账号会话无效或已过期，请重新登录",
    };

    private AccountDto Accept(Account account, string? accountSession, string? recoveryCode) => new()
    {
        Ok = true,
        Code = "ok",
        Id = account.Id.Value,
        Username = account.Username,
        DisplayName = account.DisplayName,
        AccountSession = accountSession,
        RecoveryCode = recoveryCode,
        // 只是"让前端知道现在能不能开桌"；真正的授权判定在 LobbyService 里。
        CanCreateTable = _tableCreation.CanCreate(account),
    };
}
