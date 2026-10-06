using OpenClockTower.Kernel;
using static OpenClockTower.Application.GateRejections;

namespace OpenClockTower.Application;

/// <summary>
/// **文本闸**（M4 / G-A5-8）：客户端可控文本的长度与控制字符在入口处判掉。
/// </summary>
/// <remarks>
/// <para>
/// 位置很关键：它在 <see cref="GameSession"/> 里**先于回执查询**执行——幂等键本身也在闸内，
/// 而键会直接进数据库主键。任何一条进来的命令，文本都已经是有界的。
/// </para>
/// <para>
/// 判据来自登记表 <see cref="CommandTextLimits"/>（一处声明，门禁查漏），**不在这里再写一遍上限**。
/// 控制字符这一条与说书人注记同尺（<see cref="TextNormalization.ContainsRejectedControlCharacter"/>）：
/// 换行 / 制表算可折叠空白、不算越界，其它不可见字符一律拒绝——它们是日志注入的载体。
/// </para>
/// <para>
/// 越界是**普通拒绝**（不是异常、不是崩溃）：客户端拿到人话，服务端留一条可定位的拒绝日志，
/// 状态一点没动。
/// </para>
/// </remarks>
public static class CommandTextGate
{
    /// <summary>跑一遍文本闸；越界返回拒绝，合规返回 null。</summary>
    public static CommandRejection? Check(CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (CheckIdempotencyKey(envelope.IdempotencyKey) is { } keyRejection)
        {
            return keyRejection;
        }

        if (!CommandTextLimits.Fields.TryGetValue(envelope.Command.GetType(), out var fields))
        {
            return null;
        }

        foreach (var field in fields.Where(field => field.IsBounded))
        {
            var value = field.ReadFrom(envelope.Command);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value.Length > field.MaxLength)
            {
                return Reject(
                    field.TooLong?.Code ?? "legality.text_too_long",
                    field.TooLong?.Message
                        ?? $"{FieldLabel(field)}太长（上限 {field.MaxLength} 个字符，收到 {value.Length} 个）：请精简后重试",
                    "legality");
            }

            if (ContainsRejectedControlCharacter(value, field.Control))
            {
                return Reject(
                    field.ControlViolation?.Code ?? "legality.text_control_chars",
                    field.ControlViolation?.Message
                        ?? $"{FieldLabel(field)}包含不可见控制字符（换行与制表会被折叠成空格，其它控制字符不接受）",
                    "legality");
            }
        }

        return null;
    }

    /// <summary>
    /// 控制字符判据：与说书人注记同尺（换行 / 制表算可折叠空白）或更严的一档（任何控制字符都拒绝）。
    /// </summary>
    private static bool ContainsRejectedControlCharacter(string value, CommandTextLimits.ControlRule rule) =>
        rule switch
        {
            CommandTextLimits.ControlRule.RejectAll => value.Any(char.IsControl),
            _ => TextNormalization.ContainsRejectedControlCharacter(value),
        };

    /// <summary>
    /// 幂等键：长度有界 + 不含任何控制字符（连换行也不接受——它进数据库主键与日志，不是文本字段）。
    /// </summary>
    private static CommandRejection? CheckIdempotencyKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            // 缺键由别处判（Hub 的参数形状）：这里只管"有键的时候它合不合规"。
            return null;
        }

        if (key.Length > CommandTextLimits.MaxIdempotencyKeyLength)
        {
            return Reject(
                "legality.idempotency_key_too_long",
                $"幂等键太长（上限 {CommandTextLimits.MaxIdempotencyKeyLength} 个字符，收到 {key.Length} 个）",
                "legality");
        }

        return key.Any(char.IsControl)
            ? Reject("legality.idempotency_key_control", "幂等键不能包含控制字符", "legality")
            : null;
    }

    /// <summary>字段的中文标签：命令面用的是「原因 / 说明」，日志与客户端文案都用它。</summary>
    private static string FieldLabel(CommandTextLimits.CommandTextField field) => field.PropertyName switch
    {
        "Reason" => "原因",
        "Note" => "说明",
        "Text" => "注记",
        "Question" => "问题",
        _ => field.PropertyName,
    };
}
