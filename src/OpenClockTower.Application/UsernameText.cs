namespace OpenClockTower.Application;

/// <summary>
/// 登录名口径（D-0021）：有界化 + 大小写不敏感的**同一把尺子**。
/// </summary>
/// <remarks>
/// 客户端送来的文本是不可信输入（D-0012）：去掉首尾空白后，内部空白与控制字符一律拒绝、
/// 长度有上限；唯一性比较用 <see cref="ComparisonKeyOf"/>（大小写不敏感），库里存这个键做唯一索引。
/// </remarks>
public static class UsernameText
{
    /// <summary>登录名最短长度。</summary>
    public const int MinLength = 2;

    /// <summary>登录名最长长度（归一化后按字符计）。</summary>
    public const int MaxLength = 24;

    /// <summary>
    /// 归一化并校验一个登录名：去掉首尾空白；内部空白、控制字符、空串与越界长度一律不接受。
    /// </summary>
    /// <param name="raw">客户端送来的原文（不可信，可为 null）。</param>
    /// <param name="normalized">归一化后的登录名；失败时为 <see cref="string.Empty"/>。</param>
    /// <param name="failure">机器可读失败原因：<c>empty</c> / <c>too_short</c> / <c>too_long</c> / <c>whitespace</c> / <c>control</c>；成功为空串。</param>
    public static bool TryNormalize(string? raw, out string normalized, out string failure)
    {
        normalized = string.Empty;
        failure = string.Empty;

        if (raw is null)
        {
            failure = "empty";
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            failure = "empty";
            return false;
        }

        foreach (var candidate in trimmed)
        {
            if (char.IsControl(candidate))
            {
                failure = "control";
                return false;
            }

            if (char.IsWhiteSpace(candidate))
            {
                failure = "whitespace";
                return false;
            }
        }

        if (trimmed.Length < MinLength)
        {
            failure = "too_short";
            return false;
        }

        if (trimmed.Length > MaxLength)
        {
            failure = "too_long";
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>大小写不敏感的唯一性键（存储与比较用；不改变展示用的原文）。</summary>
    public static string ComparisonKeyOf(string username) => username.ToLowerInvariant();
}
