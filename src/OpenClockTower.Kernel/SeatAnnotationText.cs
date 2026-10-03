using System.Text;

namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人注记文本的口径：归一化 + 有界化（D-0019）。
/// </summary>
/// <remarks>
/// 客户端送来的文本是不可信输入（D-0012）：这里做第一次有界化（换行折成空格、长度上限、拒绝控制字符），
/// 呈现层仍按架构 §4.4 做第二次防御，坏载荷只降级该条 token。
/// </remarks>
public static class SeatAnnotationText
{
    /// <summary>单条注记的最大长度（归一化后按字符计）。</summary>
    public const int MaxLength = 120;

    /// <summary>每个席位最多几条注记：牌面放得下，视图也有界。</summary>
    public const int MaxPerSeat = 5;

    /// <summary>
    /// 归一化并校验一条注记：折叠空白（含换行 / 制表）为单个空格、去掉首尾；
    /// 空文本、超长文本与换行 / 制表之外的控制字符一律不接受。
    /// </summary>
    /// <param name="raw">客户端送来的原文（不可信，可为 null）。</param>
    /// <param name="normalized">归一化后的文本；失败时为 <see cref="string.Empty"/>。</param>
    /// <param name="failure">机器可读失败原因：<c>empty</c> / <c>too_long</c> / <c>control</c>；成功为空串。</param>
    public static bool TryNormalize(string? raw, out string normalized, out string failure)
    {
        normalized = string.Empty;
        failure = string.Empty;

        if (raw is null)
        {
            failure = "empty";
            return false;
        }

        foreach (var candidate in raw)
        {
            if (char.IsControl(candidate) && candidate is not ('\r' or '\n' or '\t'))
            {
                failure = "control";
                return false;
            }
        }

        var collapsed = CollapseWhitespace(raw);
        if (collapsed.Length == 0)
        {
            failure = "empty";
            return false;
        }

        if (collapsed.Length > MaxLength)
        {
            failure = "too_long";
            return false;
        }

        normalized = collapsed;
        return true;
    }

    /// <summary>折叠空白：换行 / 制表 / 连续空格 → 单个空格，去掉首尾。</summary>
    private static string CollapseWhitespace(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var candidate in raw)
        {
            if (char.IsWhiteSpace(candidate))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(candidate);
        }

        return builder.ToString();
    }
}
