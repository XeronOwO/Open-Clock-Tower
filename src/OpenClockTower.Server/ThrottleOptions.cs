namespace OpenClockTower.Server;

/// <summary>
/// 账号入口限速口径（M4 / G-A1-1；前置条件是 G-A3-3 的真实客户端 IP）。
/// </summary>
/// <remarks>
/// <para>
/// 键做成**两个桶**：`客户端地址 + 登录名` 与 `客户端地址`。
/// 只按登录名，攻击者换个名字就绕过；只按 IP，同一 NAT（家里几个人一个出口）会互相连坐——
/// 两个桶同时判，取更严的那个。口径与代价见 <c>docs/decisions/active.md</c> D-0032。
/// </para>
/// <para>
/// 失败才计数（登录 / 重置），成功即清窗口：正常用户打错两次不会被拖慢，连续爆破则在阈值处停住。
/// 计的是"尝试"而不是"账号"，所以**锁不住别人的账号**——攻击者只能把自己的 IP 打进限速里。
/// </para>
/// </remarks>
public sealed class ThrottleOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:Throttle";

    /// <summary>统计窗口（秒），默认 300（5 分钟）。</summary>
    public double WindowSeconds { get; set; } = 300;

    /// <summary>同一（客户端 + 登录名）的登录失败上限，默认 5。</summary>
    public int LoginFailuresPerUsername { get; set; } = 5;

    /// <summary>同一客户端的登录失败上限（跨登录名），默认 20：拦"换着名字试"。NAT 后面的正常用户够用。</summary>
    public int LoginFailuresPerClient { get; set; } = 20;

    /// <summary>同一客户端的注册调用上限，默认 10。</summary>
    /// <remarks>计**每一次调用**（成功与失败都算）：注册不是高频动作，把额度留给真人足够。</remarks>
    public int RegisterCallsPerClient { get; set; } = 10;

    /// <summary>同一（客户端 + 登录名）的口令重置失败上限，默认 5。</summary>
    public int ResetFailuresPerUsername { get; set; } = 5;

    /// <summary>
    /// 内存里最多跟踪多少个计数桶，默认 10000。
    /// </summary>
    /// <remarks>
    /// 到上限先清过期桶；仍满则**不再为新键建桶**（既有键继续计数）。
    /// 也就是说：随机登录名的洪水打不爆内存，代价是"新键在极端时刻不被单独计数"——
    /// 那时的兜底是同一客户端那个桶，它只有一个键、必然还在。
    /// </remarks>
    public int MaxTrackedBuckets { get; set; } = 10_000;
}
