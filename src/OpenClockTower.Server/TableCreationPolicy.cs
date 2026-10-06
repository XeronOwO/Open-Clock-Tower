using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 开桌授权（D-0026）：**开桌是能力，不说书人身份**。
/// </summary>
/// <remarks>
/// <para>
/// 这里曾经把两件事绑成一个概念：只有配置里的管理员能开桌，而开桌者自动成为该桌说书人——
/// 等于"只有管理员能当说书人"。说书人是**玩这一局的角色**（谁主持这一局），不是系统权限，
/// 所以本类只回答一个窄问题：**这个账号现在能不能开一桌**。开完之后他是不是那一桌的说书人，
/// 由该桌的说书人票据认定（<see cref="LobbyService.CreateAsync"/> 把票据回给他），与本类无关。
/// </para>
/// <para>
/// 两种部署形态：
/// </para>
/// <list type="bullet">
/// <item>默认<strong>放开</strong>（<see cref="GameServerOptions.AllowPlayerTables"/> = true）：
/// 任何登录账号都能开桌——小圈子自用就该是这样。</item>
/// <item>配 <c>GameServer__AllowPlayerTables=false</c> <strong>收口</strong>：退回"只有
/// <see cref="AdminDirectory"/> 里的运维身份能开"，用于公开部署防刷桌。</item>
/// </list>
/// <para>
/// **未登录一律不能开**：开桌要记在某个账号头上（大厅列表与审计都要能说出"这是谁开的"）。
/// </para>
/// <para>
/// 放在 Server 层而不是 Application：这是部署方的授权策略，不是领域规则——与
/// <see cref="AdminDirectory"/> 同一层。
/// </para>
/// </remarks>
public sealed class TableCreationPolicy
{
    private readonly AdminDirectory _operators;
    private readonly bool _allowPlayerTables;

    /// <summary>构造开桌授权。</summary>
    /// <param name="options">宿主配置（读 <see cref="GameServerOptions.AllowPlayerTables"/>）。</param>
    /// <param name="operators">运维身份名单（收口模式下它是唯一的开桌依据）。</param>
    /// <param name="logger">日志（用于在"谁都开不了桌"这种死路上留一条可定位的告警）。</param>
    public TableCreationPolicy(GameServerOptions options, AdminDirectory operators, ILogger<TableCreationPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _operators = operators ?? throw new ArgumentNullException(nameof(operators));
        ArgumentNullException.ThrowIfNull(logger);

        _allowPlayerTables = options.AllowPlayerTables;

        // 关闭自助开桌 + 一个运维身份都没配 = **没有任何账号**能开新桌。它是合法配置，但几乎总是配错了
        // （只想关掉自助、忘了配名单），而且表现是"界面上按钮点不动"，从现象查回配置要绕很远——所以说出来。
        if (!_allowPlayerTables && !_operators.HasAnyOperator)
        {
            logger.LogWarning(
                "已关闭玩家自助开桌（{Section}:AllowPlayerTables=false），但没有配置任何运维身份（{Section}:AdminUsernames 为空）："
                + "现在没有任何账号能开新桌；要放开自助就把开关去掉，要留一个口子就配上运维登录名",
                GameServerOptions.SectionName,
                GameServerOptions.SectionName);
        }
    }

    /// <summary>部署方是否放开了玩家自助开桌（日志与文案据此区分"被谁拒的"）。</summary>
    public bool AllowsPlayerTables => _allowPlayerTables;

    /// <summary>这个账号现在能不能开桌。</summary>
    public bool CanCreate(Account? account) =>
        account is not null && (_allowPlayerTables || _operators.IsOperator(account));
}
