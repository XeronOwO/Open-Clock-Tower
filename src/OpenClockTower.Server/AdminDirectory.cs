using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 管理员判定（D-0025：只有管理员能开桌）。
/// </summary>
/// <remarks>
/// <para>
/// 依据是配置里的**登录名清单**（<see cref="GameServerOptions.AdminUsernames"/>）：登录名唯一且不可改，
/// 用它做授权键是稳定的；玩家名可以随便改，绝不能当权限依据。
/// </para>
/// <para>
/// 规则单一且安全：**清单为空 = 谁都不是管理员**。宁可不给权限，也不默认放开——
/// 这是本类唯一容易写错的地方，所以写在这里说死。
/// </para>
/// </remarks>
public sealed class AdminDirectory
{
    private readonly HashSet<string> _usernames;

    /// <summary>构造管理员名单。</summary>
    public AdminDirectory(GameServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 大小写不敏感：登录名的唯一性键本来就是大小写不敏感的（D-0021）。
        _usernames = new HashSet<string>(
            (options.AdminUsernames ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>配置里是否指定过管理员（空清单时前端不必显示"开桌"入口）。</summary>
    public bool HasAnyAdmin => _usernames.Count > 0;

    /// <summary>这个账号是不是管理员。</summary>
    public bool IsAdmin(Account? account) =>
        account is not null && _usernames.Contains(account.Username);
}
