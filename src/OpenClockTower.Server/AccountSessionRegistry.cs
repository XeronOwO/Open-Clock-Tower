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
                AccountId = accountId,
                ExpiresAt = _clock.UtcNow + Lifetime,
            });
        }

        return credential;
    }

    /// <summary>校验一条账号会话；通过则给出它所属的账号。</summary>
    public bool TryResolve(string? credential, out AccountId accountId)
    {
        accountId = default;
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
                    accountId = entry.AccountId;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>登出：撤销一条会话；原本不存在或已被清掉返回 false。</summary>
    public bool Revoke(string? credential)
    {
        if (string.IsNullOrEmpty(credential))
        {
            return false;
        }

        var hash = AccountSessionCredential.HashOf(credential);
        lock (_gate)
        {
            var removed = 0;
            for (var index = _sessions.Count - 1; index >= 0; index--)
            {
                if (CryptographicOperations.FixedTimeEquals(_sessions[index].Hash, hash))
                {
                    _sessions.RemoveAt(index);
                    removed++;
                }
            }

            return removed > 0;
        }
    }

    /// <summary>撤销某账号的全部会话（口令重置 / 改名兜底）；返回撤销条数。</summary>
    public int RevokeAllForAccount(AccountId accountId)
    {
        lock (_gate)
        {
            return _sessions.RemoveAll(entry => entry.AccountId == accountId);
        }
    }

    /// <summary>调用方必须持有 <c>_gate</c>：清掉已过期的会话。</summary>
    private void SweepExpired() =>
        _sessions.RemoveAll(entry => entry.ExpiresAt <= _clock.UtcNow);

    /// <summary>一条会话：只存哈希、账号与到期时刻。</summary>
    private sealed class Entry
    {
        /// <summary>凭据哈希。</summary>
        public required byte[] Hash { get; init; }

        /// <summary>所属账号。</summary>
        public required AccountId AccountId { get; init; }

        /// <summary>到期时刻（绝对，不滑动）。</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
