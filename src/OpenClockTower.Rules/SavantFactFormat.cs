namespace OpenClockTower.Rules;

/// <summary>候选事实在裁定文本里的编码（R-0057-C）：<c>fact:编码</c> 或 <c>fact:编码:参数</c>。</summary>
/// <remarks>
/// <para>
/// 编码要进事件流（<c>DecisionPointResolvedEvent.Decision</c>），因此必须稳定、可解析，
/// 且**不含分隔符 <c>|</c>**（两条信息用 <c>|</c> 分隔，见 R-0057 第 3 条）。
/// </para>
/// <para>
/// 参数内部允许再出现 <c>:</c>（如 <c>fact:seat-character:3:clockmaker</c>）——解析只切**第一个**冒号。
/// 不以此前缀开头的裁定文本一律按 R-0057 的**自由文本**处理（老设备 / 老客户端仍然受理）。
/// </para>
/// </remarks>
internal static class SavantFactFormat
{
    private const string Prefix = "fact:";

    /// <summary>拼一个结构化取值。</summary>
    internal static string Format(string code, string? parameter) =>
        parameter is null ? Prefix + code : $"{Prefix}{code}:{parameter}";

    /// <summary>这段裁定文本看起来是结构化编码吗（自由文本不会以此开头）。</summary>
    internal static bool LooksStructured(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>拆开结构化编码：前缀之后按**第一个**冒号切成编码与参数（无参数时参数为 null）。</summary>
    internal static bool TryParse(string value, out string code, out string? parameter)
    {
        code = string.Empty;
        parameter = null;

        if (!LooksStructured(value))
        {
            return false;
        }

        var body = value[Prefix.Length..];
        if (body.Length == 0)
        {
            return false;
        }

        var separator = body.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            code = body;
            return true;
        }

        code = body[..separator];
        parameter = body[(separator + 1)..];
        return code.Length > 0 && parameter.Length > 0;
    }
}
