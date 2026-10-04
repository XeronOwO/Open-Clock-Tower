using System.Text;

namespace OpenClockTower.Kernel;

/// <summary>
/// 文本口径的公共零件：控制字符与空白的**同一把尺子**（说书人注记 / 账号玩家名共用）。
/// </summary>
/// <remarks>
/// 纯字符串处理、无 IO；长度上限、空值语义等仍由各自的口径类决定，这里只统一
/// "什么算不合格的控制字符"与"空白怎么折叠"，避免两处口径各写一遍后慢慢漂移。
/// </remarks>
public static class TextNormalization
{
    /// <summary>是否含不接受的控制字符（换行 / 制表 / 回车按"可折叠空白"处理，不算拒绝）。</summary>
    public static bool ContainsRejectedControlCharacter(string raw)
    {
        foreach (var candidate in raw)
        {
            if (char.IsControl(candidate) && candidate is not ('\r' or '\n' or '\t'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>折叠空白：换行 / 制表 / 连续空格 → 单个空格，去掉首尾。</summary>
    public static string CollapseWhitespace(string raw)
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
