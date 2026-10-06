namespace OpenClockTower.Server;

/// <summary>
/// 自助注册的部署开关（M4 / G-A5-2）：**关掉之后没有任何自助注册路径**。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="TableCreationPolicy"/> 同一层、同一形状：这是**部署方的授权策略**，不是领域规则。
/// 公开部署被批量注册刷库时，运维可以把它关掉（<c>GameServer__AllowSelfRegistration=false</c>），
/// 关掉之后注册入口给中性拒绝码 <c>registration_closed</c> 与人话，既有账号的登录完全不受影响。
/// </para>
/// <para>
/// **它是个死开关，必须说出来**：本项目首版没有"运维替别人开账号"的入口（没有邀请码、没有管理台），
/// 所以关掉自助注册 = 从此再也开不出新账号。这是合法配置（账号已建齐的封闭部署），
/// 但几乎总是配错——启动时打一条告警，免得运维在"怎么谁都注册不了"上绕一圈。
/// </para>
/// </remarks>
public sealed class RegistrationPolicy
{
    private readonly bool _allowSelfRegistration;

    /// <summary>构造注册策略。</summary>
    /// <param name="options">宿主配置（读 <see cref="GameServerOptions.AllowSelfRegistration"/>）。</param>
    /// <param name="logger">日志（关闭时留一条可定位的告警）。</param>
    public RegistrationPolicy(GameServerOptions options, ILogger<RegistrationPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _allowSelfRegistration = options.AllowSelfRegistration;

        if (!_allowSelfRegistration)
        {
            logger.LogWarning(
                "已关闭自助注册（{Section}:AllowSelfRegistration=false）：**现在没有任何入口能开新账号**"
                + "（本项目没有邀请码与管理台）；既有账号登录不受影响。要开新账号就把它改回 true",
                GameServerOptions.SectionName);
        }
    }

    /// <summary>本服是否允许自助注册。</summary>
    public bool AllowsSelfRegistration => _allowSelfRegistration;
}
