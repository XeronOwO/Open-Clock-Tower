using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 博学者的白天提问依据（R-0057）：提示文案、两条信息的格式与结清后果。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-01 抓取）：《博学者》· 角色能力——「每个白天，你可以**私下**询问说书人
/// 以得知两条信息：一个是正确的，一个是错误的。」；· 角色简介——说书人选择两条信息、
/// 博学者不知道哪条对哪条、可以选择不要、醉酒 / 中毒时可能两条全对或两条全错。
/// </para>
/// <para>
/// 平台口径：两条信息由说书人**自由填写**，用 <c>|</c> 分隔（自由文本是用户输入——格式不合就显式拒绝，
/// 不抛异常）；平台**不判定哪条为真**（D-0002），两条都标「可能为假」，真假由说书人掌握；
/// 能力未生效（醉酒 / 中毒 / 死亡）时照样给，另加失效分类与说明（R-0004 / 三-3）；
/// 涡流在场时两条都必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class SavantQuestionSource : ISavantQuestionSource
{
    /// <summary>两条信息的分隔符（进裁定文本；提示里写明用法）。</summary>
    internal const char Separator = '|';

    /// <summary>角色标识。</summary>
    private static readonly CharacterId Savant = new("savant");

    /// <inheritdoc />
    public CharacterId Character => Savant;

    /// <inheritdoc />
    public AbilityId Ability { get; } = new("savant");

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt() => new()
    {
        Context = "博学者白天向你要两条信息：一条正确、一条错误（他不知道哪条对哪条）。"
            + $"请用「{Separator}」分隔两条，例如：3 号是镇民{Separator}5 号是爪牙。"
            + "他醉酒或中毒时两条可以都对或都错；涡流在场时两条都必须为假——"
            + "平台只如实记录你说的内容，不判定真假（百科《博学者》· 2026-10-01 抓取 · 角色能力 / 角色简介；R-0057）",
        Options = [],
        OnNoOption = NoOptionBehavior.StorytellerDecides,
    };

    /// <inheritdoc />
    public SavantQuestionResolution? Resolve(SavantQuestionResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parts = Split(context.Decision);
        if (parts is null)
        {
            return new SavantQuestionResolution
            {
                Ruling = SavantQuestionRuling.Invalid,
                Note = $"两条信息要用「{Separator}」分开写：例如 3 号是镇民{Separator}5 号是爪牙"
                    + "（两边都不能为空，也不能多写几条）",
            };
        }

        var entry = context.State.Seat(context.Question.Seat);
        var outcome = entry is null ? null : AbilityEffectivenessEvaluator.Evaluate(context.State, entry);
        if (outcome is null)
        {
            // 维度没有观测齐：判不了就不猜——由内核显式拒绝（D-0015）。
            return null;
        }

        // 博学者本人是镇民：涡流存活时他的信息必须为假（R-0028 / R-0004 对照）；
        // 处于咖啡师「清醒且健康」窗口内时不受涡流约束（R-0047 第 4 条）。
        var vortox = VortoxInterference.IsActiveFor(context.State, context.Question.Seat);
        var malfunctions = new List<MalfunctionKind>(outcome.Malfunctions);
        if (vortox)
        {
            malfunctions.Add(MalfunctionKind.Vortox);
        }

        var note = VortoxInterference.NoteFor(
            context.State,
            context.Question.Seat,
            outcome.Effective
                ? "两条信息一真一假：哪条为真由说书人掌握，平台不判定（D-0002）"
                : outcome.Note);

        return new SavantQuestionResolution
        {
            Ruling = SavantQuestionRuling.Answered,
            Effective = outcome.Effective,
            Malfunctions = malfunctions,
            Note = note,
            Events =
            [
                // 两条各发一条信息结果：收件人只有博学者本人；两条都标「可能为假」——
                // 生效时必有一条是假的，而平台不知道是哪条（不猜，D-0002）。
                new InformationResultIssuedEvent
                {
                    Recipient = context.Question.Seat,
                    Ability = Ability,
                    Content = parts[0],
                    MayBeFalse = true,
                    Note = note,
                },
                new InformationResultIssuedEvent
                {
                    Recipient = context.Question.Seat,
                    Ability = Ability,
                    Content = parts[1],
                    MayBeFalse = true,
                    Note = note,
                },
            ],
        };
    }

    /// <summary>
    /// 把裁定文本切成两条信息：必须恰好两段、各自非空；否则返回 null（由调用方转成显式拒绝）。
    /// </summary>
    private static string[]? Split(string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision))
        {
            return null;
        }

        var parts = decision.Split(Separator);
        if (parts.Length != 2)
        {
            return null;
        }

        var first = parts[0].Trim();
        var second = parts[1].Trim();
        return first.Length == 0 || second.Length == 0 ? null : [first, second];
    }
}
