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
}
