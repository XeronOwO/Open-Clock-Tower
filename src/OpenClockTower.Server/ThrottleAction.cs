namespace OpenClockTower.Server;

/// <summary>限速作用在哪个账号入口上（三个入口的预算与计数口径各不相同）。</summary>
public enum ThrottleAction
{
    /// <summary>登录：失败计数，成功清零。</summary>
    Login,

    /// <summary>注册：每次调用都计数（成功与失败都算）。</summary>
    Register,

    /// <summary>恢复码重置口令：失败计数，成功清零。</summary>
    ResetPassword,

    /// <summary>
    /// 账号注销（M5 / G-A1-6）：口令二次确认**失败**才计数，成功清零。
    /// </summary>
    /// <remarks>
    /// 注销的确认口令同样要跑一次慢哈希，所以它需要一个与登录同级的闸——
    /// 否则"拿别人的会话猜口令"就成了一条不限速的爆破入口（会话比口令好拿得多）。
    /// </remarks>
    DeleteAccount,
}
