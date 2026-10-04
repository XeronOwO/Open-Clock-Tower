namespace OpenClockTower.Application;

/// <summary>
/// 账号（D-0021）：跨局的**全局身份**——登录名解决"你是谁"，玩家名只作公开呈现。
/// </summary>
/// <remarks>
/// <para>
/// 口令与恢复码都只存 <see cref="IPasswordHasher"/> 产生的自描述哈希串，明文不落库；
/// 账号不参与任何规则判定，也不进事件流（姓名与席位绑定都是会话事实）。
/// </para>
/// <para>
/// 玩家名允许重名：它是标签，不是凭据；登录名的唯一性由 <see cref="UsernameText.ComparisonKeyOf"/>
/// 做的键保证（大小写不敏感）。
/// </para>
/// </remarks>
public sealed record Account
{
    /// <summary>账号标识。</summary>
    public required AccountId Id { get; init; }

    /// <summary>登录名（保留展示用大小写；唯一性按大小写不敏感的键比较）。</summary>
    public required string Username { get; init; }

    /// <summary>玩家名（公开呈现；允许重名）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>口令哈希（PBKDF2，见 <see cref="IPasswordHasher"/>）。</summary>
    public required string PasswordHash { get; init; }

    /// <summary>一次性恢复码哈希；null = 没有可用恢复码（重置口令后轮换）。</summary>
    public string? RecoveryCodeHash { get; init; }
}
