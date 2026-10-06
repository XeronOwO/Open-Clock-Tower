namespace OpenClockTower.Contracts;

/// <summary>
/// 账号自助结果（D-0021）：注册 / 登录 / 改玩家名 / 恢复码重置共用。
/// </summary>
/// <remarks>
/// 失败也是正常结果（<see cref="Ok"/> = false + 中性 <see cref="Message"/>），不是异常：
/// 登录类失败一律不区分"登录名不存在"与"口令不对"（拒绝回执不是账号枚举的预言机）。
/// <see cref="AccountSession"/> 与 <see cref="RecoveryCode"/> 都是**秘密**：前端只存内存、
/// 不落盘、不进日志（D-0012 / D-0021）。
/// </remarks>
public sealed record AccountDto
{
    /// <summary>是否成功。</summary>
    public required bool Ok { get; init; }

    /// <summary>机器可读结果码：ok / invalid_username / invalid_display_name / invalid_password / username_taken / invalid_credentials / invalid_recovery / invalid_session / account_missing。</summary>
    public required string Code { get; init; }

    /// <summary>中性说明（可直接展示）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>账号标识；失败时为 0。</summary>
    public int Id { get; init; }

    /// <summary>登录名；失败时为空串。</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>玩家名；失败时为空串。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>账号会话凭据：仅注册 / 登录 / 口令重置成功时返回。</summary>
    public string? AccountSession { get; init; }

    /// <summary>一次性恢复码明文：仅注册 / 口令重置成功时返回一次；服务端只存哈希。</summary>
    public string? RecoveryCode { get; init; }

    /// <summary>
    /// 这个账号现在能不能开桌（D-0026）。
    /// </summary>
    /// <remarks>
    /// 由服务端按部署开关 + 运维名单算好；前端据此决定要不要给"开桌"入口。
    /// **它不是权限**——真正的判定在开桌用例里，前端只是少显示一个按钮。
    /// 未登录一律 false：开桌要记在某个账号头上。
    /// </remarks>
    public bool CanCreateTable { get; init; }
}
