using Microsoft.Extensions.Configuration;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 管理员判定（D-0025：只有管理员能开桌）。
/// </summary>
/// <remarks>
/// <para>
/// 依据是配置里的**登录名**清单：登录名唯一且不可改，用它做授权键是稳定的；
/// 玩家名可以随便改，绝不能当权限依据。
/// </para>
/// <para>
/// 规则单一且安全：**清单为空 = 谁都不是管理员**。宁可不给权限，也不默认放开——
/// 这是本类唯一容易写错的地方，所以写在这里说死。
/// </para>
/// <para>
/// 配置形态**两种都认**（实测踩过：单个标量绑不到 `string[]`，部署时配一个管理员会静默失效）：
/// 索引式 <c>GameServer:AdminUsernames:0</c>…（推荐）与单值逗号分隔
/// <c>GameServer__AdminUsernames=a,b</c>（环境变量里最省事的写法）。
/// </para>
/// </remarks>
public sealed class AdminDirectory
{
    private readonly HashSet<string> _usernames;

    /// <summary>构造管理员名单。</summary>
    /// <param name="configuration">宿主配置（读 <see cref="GameServerOptions.SectionName"/> 下的名单）。</param>
    public AdminDirectory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection($"{GameServerOptions.SectionName}:AdminUsernames");

        // 两种形态都认，且**每个取值都可以再是逗号分隔的一串**：
        //   索引式 `AdminUsernames:0=<运维账号>`（appsettings 里最清楚）
        //   单值   `AdminUsernames=<运维账号>,alice`（环境变量里最省事）
        // 实测踩过：单个标量绑不到 `string[]`，只认索引式会让部署时配的管理员静默失效。
        var raw = section.GetChildren().Select(child => child.Value ?? string.Empty).ToArray();
        if (raw.Length == 0)
        {
            raw = [section.Value ?? string.Empty];
        }

        var names = raw
            .SelectMany(value => value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        // 大小写不敏感：登录名的唯一性键本来就是大小写不敏感的（D-0021）。
        _usernames = new HashSet<string>(
            names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>配置里是否指定过管理员（空清单时前端不必显示"开桌"入口）。</summary>
    public bool HasAnyAdmin => _usernames.Count > 0;

    /// <summary>这个账号是不是管理员。</summary>
    public bool IsAdmin(Account? account) =>
        account is not null && _usernames.Contains(account.Username);
}
