using Microsoft.Extensions.Configuration;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// **运维身份**名单（D-0026：部署级身份，与说书人无关）。
/// </summary>
/// <remarks>
/// <para>
/// 依据是配置里的**登录名**清单：登录名唯一且不可改，用它做授权键是稳定的；
/// 玩家名可以随便改，绝不能当权限依据。
/// </para>
/// <para>
/// 语义边界（这里曾经写错，所以写死）：**本类不判定"谁是这一局的说书人"**。
/// 说书人是"主持这一局的人"，由该桌的说书人票据认定，任何登录玩家开一桌就得到它。
/// 本类只回答"这个账号是不是部署方指定的人"，当前唯一用途是
/// <see cref="GameServerOptions.AllowPlayerTables"/> 关掉后的开桌兜底（见 <see cref="TableCreationPolicy"/>），
/// 将来用于关桌 / 清场这类部署级动作。
/// </para>
/// <para>
/// 规则单一且安全：**清单为空 = 谁都不是运维身份**。宁可不给权限，也不默认放开。
/// </para>
/// <para>
/// 配置形态**两种都认**（实测踩过：单个标量绑不到 `string[]`，部署时配一个运维会静默失效）：
/// 索引式 <c>GameServer:AdminUsernames:0</c>…（推荐）与单值逗号分隔
/// <c>GameServer__AdminUsernames=a,b</c>（环境变量里最省事的写法）。
/// </para>
/// </remarks>
public sealed class AdminDirectory
{
    private readonly HashSet<string> _usernames;

    /// <summary>构造运维名单。</summary>
    /// <param name="configuration">宿主配置（读 <see cref="GameServerOptions.SectionName"/> 下的名单）。</param>
    public AdminDirectory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection($"{GameServerOptions.SectionName}:AdminUsernames");

        // 两种形态都认，且**每个取值都可以再是逗号分隔的一串**：
        //   索引式 `AdminUsernames:0=<运维账号>`（appsettings 里最清楚）
        //   单值   `AdminUsernames=<运维账号>,alice`（环境变量里最省事）
        // 实测踩过：单个标量绑不到 `string[]`，只认索引式会让部署时配的名单静默失效。
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

    /// <summary>这个账号是不是部署方指定的运维身份。</summary>
    public bool IsOperator(Account? account) =>
        account is not null && _usernames.Contains(account.Username);

    /// <summary>
    /// 配置里是否指定过运维身份。
    /// </summary>
    /// <remarks>
    /// 只有 <see cref="TableCreationPolicy"/> 用它判"关闭自助开桌 + 一个人都没配 = 没人开得出新桌"这条死路，
    /// 并在启动时把话说出来——静默地谁都开不了桌是最难查的一种部署错误。
    /// </remarks>
    public bool HasAnyOperator => _usernames.Count > 0;
}
