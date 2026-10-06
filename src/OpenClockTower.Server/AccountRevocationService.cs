using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 账号撤销（M2 / G-A2-1）：撤销账号会话与**由它授权的在线连接**是同一件事，必须同批完成。
/// </summary>
/// <remarks>
/// <para>
/// 拆出来之前，登出只调会话表的撤销：撤销停在"下一次进门"，已经进门的连接继续有效
/// （运行时读数见 `docs/security/web-hardening-audit.md` 的 G-A2-1）。根因不是"少踢了一脚"，
/// 而是连接凭据与授权它的会话之间**没有链路**，撤销根本没有路径打到连接上。
/// </para>
/// <para>
/// 所以撤销只留一个入口：会话表与连接表由这里一起动，调用方无从只做一半。
/// 粒度与会话一一对应——登出撤一条会话，只踢那条会话建立的连接（同账号在别的设备上的登录不受牵连）；
/// 口令重置撤该账号全部会话，于是该账号的在线连接全部失效。
/// </para>
/// <para>
/// 撤的是**这条连接的身份**，不是席位归属：席位认领在存储里，重新登录回来还能坐回原位（D-0021）。
/// </para>
/// </remarks>
public sealed class AccountRevocationService
{
    private readonly AccountSessionRegistry _sessions;
    private readonly ConnectionRegistry _connections;
    private readonly ILogger<AccountRevocationService> _logger;

    /// <summary>构造撤销编排。</summary>
    public AccountRevocationService(
        AccountSessionRegistry sessions,
        ConnectionRegistry connections,
        ILogger<AccountRevocationService> logger)
    {
        _sessions = sessions;
        _connections = connections;
        _logger = logger;
    }

    /// <summary>登出：撤一条账号会话，并踢掉由它建立的全部在线连接；返回是否真的撤到了一条会话。</summary>
    /// <remarks>
    /// 顺序有意为之：**先撤会话**（此后任何入口都不再认它，也不会再签发新连接），再撤连接。
    /// 反过来会留下一个窗口：连接被踢、会话还在，玩家可以立刻用同一串凭据重新入座。
    /// </remarks>
    public bool RevokeSession(string? accountSession)
    {
        if (!_sessions.TryResolveSession(accountSession, out var session))
        {
            // 幂等：重复登出不该报错（客户端也可能重试）。
            _logger.LogInformation(
                "登出未撤销任何会话（无效或已失效）：会话指纹={Fingerprint}",
                AccountSessionCredential.FingerprintOf(accountSession));
            return false;
        }

        var revoked = _sessions.Revoke(session);
        var connections = _connections.RevokeSession(session);
        _logger.LogInformation(
            "已登出：account={AccountId} 会话={Session} 已撤销会话={Revoked} 已踢连接数={Connections} 连接={ConnectionIds}",
            session.Account.Value,
            session,
            revoked,
            connections.Count,
            string.Join(",", connections));
        return revoked;
    }

    /// <summary>口令重置（账号级失效）：撤该账号全部会话，并踢掉它们建立的全部在线连接；返回撤销的会话条数。</summary>
    public int RevokeAllForAccount(AccountId accountId)
    {
        var sessions = _sessions.RevokeAllForAccount(accountId);
        var connections = _connections.RevokeAccount(accountId);
        _logger.LogInformation(
            "已撤销账号全部会话：account={AccountId} 已撤销会话={Sessions} 已踢连接数={Connections} 连接={ConnectionIds}",
            accountId.Value,
            sessions,
            connections.Count,
            string.Join(",", connections));
        return sessions;
    }
}
