using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 玩家名口径（D-0021）：归一化 + 有界化，与说书人注记文本共用同一把空白 / 控制字符尺子。
/// </summary>
/// <remarks>
/// 客户端送来的文本是不可信输入（D-0012）：这里做第一次有界化（换行折成空格、长度上限、
/// 拒绝控制字符），呈现层仍按架构 §4.4 做第二次防御、坏载荷只降级该处显示。
/// 玩家名允许重名，这里**不做**唯一性判定。
/// </remarks>
public static class DisplayNameText
{
    /// <summary>玩家名最长长度（归一化后按字符计）。</summary>
    public const int MaxLength = 24;

    /// <summary>
    /// 归一化并校验一个玩家名：折叠空白（含换行 / 制表）为单个空格、去掉首尾；
    /// 空文本、超长文本与换行 / 制表之外的控制字符一律不接受。
    /// </summary>
    /// <param name="raw">客户端送来的原文（不可信，可为 null）。</param>
    /// <param name="normalized">归一化后的玩家名；失败时为 <see cref="string.Empty"/>。</param>
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

        if (TextNormalization.ContainsRejectedControlCharacter(raw))
        {
            failure = "control";
            return false;
        }

        var collapsed = TextNormalization.CollapseWhitespace(raw);
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
}
