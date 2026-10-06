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
/// </remarks>
public sealed class AccountHub : Hub
{
    private readonly AccountService _accounts;
    private readonly AccountSessionRegistry _sessions;
    private readonly GameRegistry _games;
    private readonly NotificationDispatcher _dispatcher;
    private readonly LobbyService _lobby;
    private readonly TableCreationPolicy _tableCreation;
    private readonly ILogger<AccountHub> _logger;

    /// <summary>构造账号 Hub。</summary>
    /// <remarks>
    /// 多桌（D-0024）：改名要同步到**该账号所在的每一桌**，所以这里拿注册表逐桌更新，
    /// 而不是拿一份进程级的席位名读模型（那份已随单例一起删除）。
    /// 大厅（D-0025）也挂在这里：账号是入场凭据，大厅与账号自助是同一层的事。
    /// </remarks>
    public AccountHub(
        AccountService accounts,
        AccountSessionRegistry sessions,
        GameRegistry games,
        NotificationDispatcher dispatcher,
        LobbyService lobby,
        TableCreationPolicy tableCreation,
        ILogger<AccountHub> logger)
    {
        _accounts = accounts;
        _sessions = sessions;
        _games = games;
        _dispatcher = dispatcher;
        _lobby = lobby;
        _tableCreation = tableCreation;
        _logger = logger;
    }

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
    public async Task<AccountDto> Register(string username, string displayName, string password)
    {
        var outcome = await _accounts.RegisterAsync(username, displayName, password, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _logger.LogInformation(
                "注册被拒：connection={ConnectionId} code={Code} 原因={Message}",
                Context.ConnectionId,
                outcome.Code,
                outcome.Message);
            return Reject(outcome);
        }

        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "账号已注册并登录：account={AccountId} username={Username} connection={ConnectionId} 会话指纹={Fingerprint}",
            outcome.Account.Id,
            outcome.Account.Username,
            Context.ConnectionId,
            AccountSessionCredential.FingerprintOf(session.Value));
        return Accept(outcome.Account, session.Value, outcome.RecoveryCode);
    }

    /// <summary>登录：签发账号会话；失败一律中性文案（不暴露登录名是否存在）。</summary>
    public async Task<AccountDto> Login(string username, string password)
    {
        var outcome = await _accounts.AuthenticateAsync(username, password, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _logger.LogWarning(
                "登录被拒：connection={ConnectionId} code={Code}",
                Context.ConnectionId,
                outcome.Code);
            return Reject(outcome);
        }

        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "已登录：account={AccountId} username={Username} connection={ConnectionId} 会话指纹={Fingerprint}",
            outcome.Account.Id,
            outcome.Account.Username,
            Context.ConnectionId,
            AccountSessionCredential.FingerprintOf(session.Value));
        return Accept(outcome.Account, session.Value, recoveryCode: null);
    }

    /// <summary>登出：撤销这条账号会话（幂等：已失效也返回成功，只是说明不同）。</summary>
    public Task<AccountDto> Logout(string accountSession)
    {
        var revoked = _sessions.Revoke(accountSession);
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

    /// <summary>恢复码重置口令（D-0021）：成功后撤销该账号全部旧会话、轮换恢复码，并直接重新登录。</summary>
    public async Task<AccountDto> ResetPassword(string username, string recoveryCode, string newPassword)
    {
        var outcome = await _accounts.ResetPasswordAsync(username, recoveryCode, newPassword, Context.ConnectionAborted);
        if (!outcome.Accepted || outcome.Account is null)
        {
            _logger.LogWarning(
                "口令重置被拒：connection={ConnectionId} code={Code}",
                Context.ConnectionId,
                outcome.Code);
            return Reject(outcome);
        }

        var revoked = _sessions.RevokeAllForAccount(outcome.Account.Id);
        var session = _sessions.Issue(outcome.Account.Id);
        _logger.LogInformation(
            "口令已重置：account={AccountId} username={Username} 已撤销旧会话={Revoked} 已重新登录",
            outcome.Account.Id,
            outcome.Account.Username,
            revoked);
        return Accept(outcome.Account, session.Value, outcome.RecoveryCode);
    }

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
