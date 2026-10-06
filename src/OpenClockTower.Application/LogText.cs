namespace OpenClockTower.Application;

/// <summary>
/// 客户端可控文本进日志前的**唯一出口**（M4 / G-A5-10）：截断 + 控制字符转义。
/// </summary>
/// <remarks>
/// <para>
/// 审计把这一类问题叫"日志注入与膨胀"：客户端可控的无界字符串（幂等键、作废原因、桌名、
/// 未知枚举名…）一旦原样进日志，就能用换行**伪造日志行**（"看起来像系统写的"），
/// 也能用超长串把日志文件灌大。两者都不需要任何凭据。
/// </para>
/// <para>
/// 口径：**先转义、后截断**，所以结果一定单行、且长度有界（换行 → 反斜杠 + `n`、制表 → 反斜杠 + `t`、
/// 其它控制字符 → 反斜杠 + `x` + 两位十六进制）。转义而不是丢弃，是为了让"有人在灌日志"这件事在日志里看得见。
/// </para>
/// <para>
/// **这是最后一道兜底，不是第一道**：真正的防线是 <see cref="CommandTextGate"/> 在入口处
/// 拒绝超长与非法文本（M4 / G-A5-8）。两处都做是有意的——闸门可能漏，日志不能。
/// </para>
/// </remarks>
public static class LogText
{
    /// <summary>日志字段的默认上限（字符数，转义后计）。</summary>
    public const int MaxLength = 120;

    /// <summary>空值占位符：与"空串"区分开，免得日志里出现 `key=` 这种分不出是空还是没记的形态。</summary>
    public const string Empty = "(空)";

    /// <summary>把一段客户端可控文本变成可以安全写进一行日志的形态。</summary>
    /// <param name="value">原文（可为 null）。</param>
    /// <param name="maxLength">上限；超出部分截断并加省略号。</param>
    public static string Clamp(string? value, int maxLength = MaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Empty;
        }

        var builder = new System.Text.StringBuilder(Math.Min(value.Length, maxLength) + 1);
        foreach (var candidate in value)
        {
            var piece = Escape(candidate);
            if (builder.Length + piece.Length > maxLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(piece);
        }

        return builder.ToString();
    }

    /// <summary>转义单个字符：换行 / 制表 / 回车给出可读形状，其它控制字符给出码位。</summary>
    /// <remarks>前缀写成常量而不是字面量：一条转义规则只有一个来源，读起来也不用数反斜杠。</remarks>
    private static string Escape(char candidate) => candidate switch
    {
        '\n' => $"{EscapePrefix}n",
        '\r' => $"{EscapePrefix}r",
        '\t' => $"{EscapePrefix}t",
        _ when char.IsControl(candidate) => $"{EscapePrefix}x{(int)candidate:X2}",
        _ => candidate.ToString(),
    };

    /// <summary>转义前缀（一个反斜杠）。</summary>
    private const char EscapePrefix = '\\';
}
