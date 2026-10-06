using System.Security.Cryptography;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 账号会话注册表（D-0021）：登录 → 凭据（只存哈希）→ 认领席位 / 账号自助；有过期、可撤销。
/// </summary>
/// <remarks>
/// <para>
/// 会话只在内存里，进程重启即失效（重新登录即可）——它不是游戏状态，不进事件流、不落盘。
/// 比较用固定时间算法、逐条扫（会话数很小）；日志只允许写短指纹（调用方负责）。
/// </para>
/// <para>
/// 撤销面：登出撤一条；口令重置撤该账号的全部会话（旧登录不能再认领或改资料）。
/// **撤销不止于此**（M2 / G-A2-1）：每条会话在签发时拿到一个进程内标识（<see cref="AccountSessionRef"/>），
/// 连接级凭据把它记在自己身上，于是"这条会话被撤"能打到**已经进门的那条连接**上。
/// 同批动作收在 <see cref="AccountRevocationService"/>：撤销请走它，不要只调这里。
/// </para>
/// </remarks>
public sealed class AccountSessionRegistry
{
    /// <summary>会话有效期（绝对值，不滑动）。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly object _gate = new();
    private readonly List<Entry> _sessions = [];
    private readonly IClock _clock;

    /// <summary>构造注册表。</summary>
    public AccountSessionRegistry(IClock clock) => _clock = clock;

    /// <summary>签发一条账号会话；返回明文凭据（服务端只保存它的哈希）。</summary>
    public AccountSessionCredential Issue(AccountId accountId)
    {
        var credential = AccountSessionCredential.CreateNew();
        lock (_gate)
        {
            SweepExpired();
            _sessions.Add(new Entry
            {
                Hash = AccountSessionCredential.HashOf(credential.Value),
                Session = AccountSessionRef.CreateNew(accountId),
                ExpiresAt = _clock.UtcNow + Lifetime,
            });
        }

        return credential;
    }

    /// <summary>校验一条账号会话；通过则给出它所属的账号。</summary>
    public bool TryResolve(string? credential, out AccountId accountId)
    {
        if (TryResolveSession(credential, out var session))
        {
            accountId = session.Account;
            return true;
        }

        accountId = default;
        return false;
    }

    /// <summary>校验一条账号会话；通过则给出它的**服务端引用**（账号 + 进程内会话标识）。</summary>
    /// <remarks>加入路径用它：连接级凭据要记住"是哪条会话授权了我"（M2 / G-A2-1）。</remarks>
    public bool TryResolveSession(string? credential, out AccountSessionRef session)
    {
        session = default;
        if (string.IsNullOrEmpty(credential))
        {
            return false;
        }

        var hash = AccountSessionCredential.HashOf(credential);
        lock (_gate)
        {
            SweepExpired();
            foreach (var entry in _sessions)
            {
                if (CryptographicOperations.FixedTimeEquals(entry.Hash, hash))
                {
                    session = entry.Session;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>登出：按凭据撤销一条会话；原本不存在或已被清掉返回 false。</summary>
    public bool Revoke(string? credential) =>
        TryResolveSession(credential, out var session) && Revoke(session);

    /// <summary>按服务端引用撤销一条会话（调用方已经解析过凭据时用它，省一次线性扫描）。</summary>
    public bool Revoke(AccountSessionRef session)
    {
        lock (_gate)
        {
            return _sessions.RemoveAll(entry => entry.Session == session) > 0;
        }
    }

    /// <summary>撤销某账号的全部会话（口令重置 / 账号级失效）；返回撤销条数。</summary>
    public int RevokeAllForAccount(AccountId accountId)
    {
        lock (_gate)
        {
            return _sessions.RemoveAll(entry => entry.Session.Account == accountId);
        }
    }

    /// <summary>调用方必须持有 <c>_gate</c>：清掉已过期的会话。</summary>
    private void SweepExpired() =>
        _sessions.RemoveAll(entry => entry.ExpiresAt <= _clock.UtcNow);

    /// <summary>一条会话：只存哈希、服务端引用与到期时刻。</summary>
    private sealed class Entry
    {
        /// <summary>凭据哈希。</summary>
        public required byte[] Hash { get; init; }

        /// <summary>服务端引用（账号 + 进程内会话标识）。</summary>
        public required AccountSessionRef Session { get; init; }

        /// <summary>到期时刻（绝对，不滑动）。</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
