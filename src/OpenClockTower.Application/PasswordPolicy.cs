namespace OpenClockTower.Application;

/// <summary>
/// 口令口径（D-0021）：只做有界化与最低强度，不猜用户的组成习惯、不做复杂度规则。
/// </summary>
/// <remarks>客户端送来的原文是不可信输入（D-0012）：先量长度与字符面，再交给哈希器。</remarks>
public static class PasswordPolicy
{
    /// <summary>口令最短长度（按字符计）。</summary>
    public const int MinLength = 8;

    /// <summary>口令最长长度（按字符计）。</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// 校验一个口令：长度 8–128、不含控制字符；空值不接受。
    /// </summary>
    /// <param name="raw">客户端送来的原文（不可信，可为 null）。</param>
    /// <param name="failure">机器可读失败原因：<c>empty</c> / <c>too_short</c> / <c>too_long</c> / <c>control</c>；成功为空串。</param>
    public static bool TryValidate(string? raw, out string failure)
    {
        failure = string.Empty;

        if (raw is null || raw.Length == 0)
        {
            failure = "empty";
            return false;
        }

        foreach (var candidate in raw)
        {
            if (char.IsControl(candidate))
            {
                failure = "control";
                return false;
            }
        }

        if (raw.Length < MinLength)
        {
            failure = "too_short";
            return false;
        }

        if (raw.Length > MaxLength)
        {
            failure = "too_long";
            return false;
        }

        return true;
    }
}
