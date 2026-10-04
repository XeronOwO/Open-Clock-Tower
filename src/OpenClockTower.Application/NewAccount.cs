namespace OpenClockTower.Application;

/// <summary>注册草稿（D-0021）：策略校验与哈希已完成，等账号表签发标识。</summary>
public sealed record NewAccount
{
    /// <summary>归一化后的登录名。</summary>
    public required string Username { get; init; }

    /// <summary>归一化后的玩家名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>口令哈希。</summary>
    public required string PasswordHash { get; init; }

    /// <summary>一次性恢复码哈希。</summary>
    public required string RecoveryCodeHash { get; init; }
}
