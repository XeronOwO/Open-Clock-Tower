namespace OpenClockTower.Application;

/// <summary>
/// 账号操作结果（D-0021）：拒绝也要带机器可读原因，供 Server 写审计与中性文案。
/// </summary>
/// <remarks>
/// 登录类失败一律收敛成 <c>invalid_credentials</c> / <c>invalid_recovery</c> 这类**中性**码，
/// 不区分"登录名不存在"与"口令不对"——拒绝回执不许当账号枚举的预言机。
/// </remarks>
public sealed record AccountOutcome
{
    /// <summary>是否通过。</summary>
    public required bool Accepted { get; init; }

    /// <summary>机器可读结果码：<c>ok</c> / <c>invalid_username</c> / <c>invalid_display_name</c> / <c>invalid_password</c> / <c>username_taken</c> / <c>invalid_credentials</c> / <c>invalid_recovery</c> / <c>account_missing</c>。</summary>
    public required string Code { get; init; }

    /// <summary>中性说明（可直接展示；不泄露"这个登录名是否存在"）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>操作后的账号（注册 / 登录 / 改名 / 重置成功时非空）。</summary>
    public Account? Account { get; init; }

    /// <summary>一次性恢复码明文：只在注册 / 重置成功时下发一次，服务端只存哈希。</summary>
    public string? RecoveryCode { get; init; }
}
