using System.Reflection;
using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// **命令里客户端可控文本的登记表**（M4 / G-A5-8）：每个字段要么有长度上限，要么写明豁免理由。
/// </summary>
/// <remarks>
/// <para>
/// 审计点到的缺口是"长度只覆盖一半"：`note` / `reason` / 幂等键**连长度都没有**，一路透传到落库与日志。
/// 这一张表就是那道尺子——<see cref="CommandTextGate"/> 按它判，门禁
/// （`tests/OpenClockTower.NormativeGates.Tests/CommandTextFieldGateTests.cs`）按它查漏：
/// **新增一个 string 字段而不在这里表态，门禁直接变红**。
/// </para>
/// <para>
/// 两类行：**有上限的**是自由文本（人写的、会进事件流与日志）；**豁免的**不是自由文本
/// （枚举名 / 服务端给的选项值 / 服务端签发的标识）——取值由别的闸按白名单判定，长度由
/// `MaximumReceiveMessageSize` 兜底，进日志前另经 <see cref="LogText.Clamp"/>。
/// **豁免必须写理由**：不写理由的豁免等于没表态。
/// </para>
/// <para>
/// 上限的来源：注记与艺术家提问**复用内核那一把尺子**（<see cref="SeatAnnotationText.MaxLength"/> /
/// <see cref="ArtistQuestionText.MaxLength"/>）——闸与内核各写一个数，迟早会漂移。
/// </para>
/// </remarks>
public static class CommandTextLimits
{
    /// <summary>说书人自由文本（原因 / 说明）的字符上限：200。</summary>
    /// <remarks>
    /// 取值口径：这些文本会**落进事件流**（复盘里看得见）并进日志，所以既要够写清楚一句判断，
    /// 又不能让一次命令塞进一篇长文——一整段对话不属于这里。
    /// </remarks>
    public const int MaxFreeTextLength = 200;

    /// <summary>幂等键的字符上限：64。</summary>
    /// <remarks>
    /// 键由客户端生成（`前缀:crypto.randomUUID()` ≈ 45 字符）。它进数据库主键、进回执、进日志，
    /// 所以**必须有界**；64 给前缀留了余量，又不给"拿幂等键当传输通道"留空间。
    /// </remarks>
    public const int MaxIdempotencyKeyLength = 64;

    /// <summary>注记超长的拒绝口径：与 D-0019 落地的那些用例、文档保持一致，不另起新码。</summary>
    private static readonly CommandTextViolation AnnotationTooLong = new(
        "legality.annotation_too_long",
        $"注记最多 {SeatAnnotationText.MaxLength} 个字符");

    /// <summary>注记含控制字符的拒绝口径：同上。</summary>
    private static readonly CommandTextViolation AnnotationControl = new(
        "legality.annotation_control",
        "注记不能包含控制字符");

    private static readonly IReadOnlyDictionary<Type, IReadOnlyList<CommandTextField>> Registry = Build(
        (typeof(AskArtistQuestionCommand),
        [
            Bounded(nameof(AskArtistQuestionCommand.Question), ArtistQuestionText.MaxLength)
                with
                {
                    // 提问的口径与内核同尺（ArtistQuestionMachine）：任何控制字符都拒绝、文案也照旧。
                    Control = ControlRule.RejectAll,
                    TooLong = new CommandTextViolation(
                        "artist.question_too_long",
                        $"问题太长（上限 {ArtistQuestionText.MaxLength} 个字符）：请精简后重问"),
                    ControlViolation = new CommandTextViolation(
                        "artist.question_control",
                        "问题不能包含控制字符：请用普通空格分隔"),
                },
        ]),
        (typeof(AddSeatAnnotationCommand),
        [
            Bounded(nameof(AddSeatAnnotationCommand.Text), SeatAnnotationText.MaxLength)
                with { TooLong = AnnotationTooLong, ControlViolation = AnnotationControl },
        ]),
        (typeof(UpdateSeatAnnotationCommand),
        [
            Bounded(nameof(UpdateSeatAnnotationCommand.Text), SeatAnnotationText.MaxLength)
                with { TooLong = AnnotationTooLong, ControlViolation = AnnotationControl },
        ]),
        (typeof(VoidRequestCommand), [Bounded(nameof(VoidRequestCommand.Note), MaxFreeTextLength)]),
        (typeof(ResolveDecisionPointCommand),
        [
            Bounded(nameof(ResolveDecisionPointCommand.Note), MaxFreeTextLength),
            Exempt(nameof(ResolveDecisionPointCommand.Decision), "裁定选项取值由内核按白名单判定，不是自由文本"),
        ]),
        (typeof(ProxyFillCommand),
        [
            Bounded(nameof(ProxyFillCommand.Note), MaxFreeTextLength),
            Exempt(nameof(ProxyFillCommand.OptionValue), "代填选项来自服务端下发的合法选项集合（Prompt.IsLegalAnswer）"),
        ]),
        (typeof(SubmitResponseCommand),
            [Exempt(nameof(SubmitResponseCommand.OptionValue), "响应选项来自服务端下发的合法选项集合（Prompt.IsLegalAnswer）")]),
        (typeof(ApplySeatStateCommand), [Bounded(nameof(ApplySeatStateCommand.Reason), MaxFreeTextLength)]),
        (typeof(ForceAdvanceCommand), [Bounded(nameof(ForceAdvanceCommand.Reason), MaxFreeTextLength)]),
        (typeof(TakeOverCommand), [Bounded(nameof(TakeOverCommand.Reason), MaxFreeTextLength)]),
        (typeof(ReleaseControlCommand), [Bounded(nameof(ReleaseControlCommand.Reason), MaxFreeTextLength)]),
        (typeof(RebuildRoomCommand), [Bounded(nameof(RebuildRoomCommand.Reason), MaxFreeTextLength)]),
        (typeof(RemoveTravellerCommand), [Bounded(nameof(RemoveTravellerCommand.Note), MaxFreeTextLength)]),
        (typeof(ResolveDayProtectionCommand), [Bounded(nameof(ResolveDayProtectionCommand.Note), MaxFreeTextLength)]),
        (typeof(PunishExecutionCommand), [Bounded(nameof(PunishExecutionCommand.Note), MaxFreeTextLength)]),
        (typeof(PitHagCasualtyCommand), [Bounded(nameof(PitHagCasualtyCommand.Note), MaxFreeTextLength)]),
        (typeof(ResolveDeferredDeathCommand), [Bounded(nameof(ResolveDeferredDeathCommand.Note), MaxFreeTextLength)]));

    /// <summary>登记表：命令类型 → 该类型上所有客户端可控 string 字段。</summary>
    /// <remarks>每条命令**只登记一次**：登记表里没有的类型，说明它一个 string 字段都没有（门禁按此核对）。</remarks>
    public static IReadOnlyDictionary<Type, IReadOnlyList<CommandTextField>> Fields => Registry;

    /// <summary>这条命令是否带客户端自由文本（写文本的动作限速按它分类，M4 / G-A5-8 的频率半）。</summary>
    public static bool CarriesFreeText(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Registry.TryGetValue(command.GetType(), out var fields) && fields.Any(field => field.IsBounded);
    }

    /// <summary>有上限的字段行。</summary>
    private static CommandTextField Bounded(string propertyName, int maxLength) => new(propertyName, maxLength);

    /// <summary>豁免的字段行（必须写理由）。</summary>
    private static CommandTextField Exempt(string propertyName, string reason) =>
        new(propertyName, MaxLength: 0) { ExemptionReason = reason };

    /// <summary>把登记表编译成"属性句柄 + 上限"：字段名写错在这里当场抛，不拖到运行期。</summary>
    private static Dictionary<Type, IReadOnlyList<CommandTextField>> Build(
        params (Type Command, CommandTextField[] Fields)[] entries)
    {
        var map = new Dictionary<Type, IReadOnlyList<CommandTextField>>(entries.Length);
        foreach (var (command, fields) in entries)
        {
            foreach (var field in fields)
            {
                field.Property = command.GetProperty(
                    field.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException(
                        $"文本登记表写错了：{command.Name} 上没有 {field.PropertyName} 这个公开属性");
            }

            map[command] = fields;
        }

        return map;
    }

    /// <summary>登记表里的一行：字段名 + 上限（豁免行给理由、不给上限）。</summary>
    public sealed record CommandTextField(string PropertyName, int MaxLength)
    {
        /// <summary>非 null = 本字段豁免长度检查，这里写明为什么。</summary>
        public string? ExemptionReason { get; init; }

        /// <summary>越界时用的拒绝码与文案；不给就走通用文案（`legality.text_too_long`）。</summary>
        public CommandTextViolation? TooLong { get; init; }

        /// <summary>含控制字符时用的拒绝码与文案；不给就走通用文案（`legality.text_control_chars`）。</summary>
        public CommandTextViolation? ControlViolation { get; init; }

        /// <summary>控制字符口径（默认：换行 / 制表算可折叠空白，其它控制字符拒绝）。</summary>
        public ControlRule Control { get; init; } = ControlRule.RejectExceptWhitespace;

        /// <summary>属性句柄（构建登记表时解析；业务代码只读）。</summary>
        public PropertyInfo Property { get; internal set; } = null!;

        /// <summary>本行是否受长度约束。</summary>
        public bool IsBounded => ExemptionReason is null;

        /// <summary>取这条命令上的字段值（登记表保证属性存在）。</summary>
        public string? ReadFrom(GameCommand command) => (string?)Property.GetValue(command);

        /// <summary>给人看的说明（门禁报红时点名用）。</summary>
        public string Describe() => IsBounded
            ? $"{PropertyName}（上限 {MaxLength} 字符）"
            : $"{PropertyName}（豁免：{ExemptionReason}）";
    }

    /// <summary>越界时的拒绝码与文案。</summary>
    /// <param name="Code">机器可读拒绝码。</param>
    /// <param name="Message">给玩家看的人话；null = 由闸按默认文案拼。</param>
    public sealed record CommandTextViolation(string Code, string? Message);

    /// <summary>字段的控制字符口径。</summary>
    public enum ControlRule
    {
        /// <summary>换行 / 制表算可折叠空白（注记与事件流里的自由文本用这一档）。</summary>
        RejectExceptWhitespace,

        /// <summary>任何控制字符都拒绝（单行字段用这一档，如艺术家提问）。</summary>
        RejectAll,
    }
}
