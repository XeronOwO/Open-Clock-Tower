namespace OpenClockTower.Server;

/// <summary>账号表行（D-0021）：全局身份，跨局持久。</summary>
/// <remarks>
/// 登录名的唯一性由 <see cref="UsernameKey"/>（大小写不敏感的比较键）上的唯一索引保证；
/// 口令与恢复码只存自描述哈希串，明文绝不落库。
/// </remarks>
public sealed class UserEntity
{
    /// <summary>账号标识（自增主键）。</summary>
    public int Id { get; set; }

    /// <summary>登录名的大小写不敏感比较键（唯一索引）。</summary>
    public string UsernameKey { get; set; } = string.Empty;

    /// <summary>登录名（展示用原文）。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>玩家名（公开呈现；允许重名）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>口令哈希。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>一次性恢复码哈希；null = 没有可用恢复码。</summary>
    public string? RecoveryCodeHash { get; set; }
}
