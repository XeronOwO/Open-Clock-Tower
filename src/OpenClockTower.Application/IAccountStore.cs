namespace OpenClockTower.Application;

/// <summary>
/// 账号表（D-0021）：账号是全局身份、跨局持久；登录名的唯一性按大小写不敏感的比较键做唯一索引。
/// </summary>
public interface IAccountStore
{
    /// <summary>按登录名查（大小写不敏感）；没有返回 null。</summary>
    Task<Account?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>按标识查；没有返回 null。</summary>
    Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken);

    /// <summary>创建账号；登录名已被占用（含并发竞态）返回 null，不抛异常。</summary>
    Task<Account?> TryCreateAsync(NewAccount account, CancellationToken cancellationToken);

    /// <summary>整份更新（玩家名 / 口令 / 恢复码）；账号不存在返回 false。</summary>
    Task<bool> TryUpdateAsync(Account account, CancellationToken cancellationToken);
}
