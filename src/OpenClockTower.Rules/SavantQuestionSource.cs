using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 博学者的白天提问依据（R-0057 / R-0057-C）：候选事实库、真值求值、组合校验与结清后果。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-01 抓取）：《博学者》· 角色能力——「每个白天，你可以**私下**询问说书人
/// 以得知两条信息：一个是正确的，一个是错误的。」；· 角色简介——说书人选择两条信息、
/// 博学者不知道哪条对哪条、可以选择不要、醉酒 / 中毒时可能两条全对或两条全错；
/// 《涡流》· 角色简介 / 运作方式——「哪怕他们醉酒或中毒，信息也一定是错误的」。
/// </para>
/// <para>
/// 平台口径（R-0057 / R-0057-C）：提示里**摆出候选事实与它们的真值**（平台按当时的账求值，
/// 判不了的不进候选），说书人两个槽位各挑一条；提交时按**当时的账**重新求值与核对组合
/// （生效 → 恰一真一假；醉酒 / 中毒 / 死亡 → 任意组合；涡流在场 → 两条都必须为假）。
/// 平台仍然**不替说书人挑内容**（D-0002）：候选只是摆在他面前，用不用、给不给由他定；
/// 自由文本（用 <c>|</c> 分隔）继续受理，但显式标「未校验」。
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
    public ChoicePrompt BuildPrompt(SavantPromptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var world = World(context.State, context.Seats);
        var (rule, note) = RuleFor(context.State, context.Seat);

        return new ChoicePrompt
        {
            Context = "博学者白天向你要两条信息：一条正确、一条错误（他不知道哪条对哪条）。"
                + "下面按当前账算好了候选事实与各自真假：第一条、第二条各挑一条即可；"
                + $"也可以不理候选，自己写两条（用「{Separator}」分隔，平台不校验真假）。"
                + "（百科《博学者》· 2026-10-01 抓取 · 角色能力 / 角色简介；R-0057 / R-0057-C）",
            Options = BuildOptions(world),
            Audience = ChoiceAudience.Storyteller,
            OnNoOption = NoOptionBehavior.StorytellerDecides,
            TruthRule = rule,
            TruthNote = note,
        };
    }

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

        var world = World(context.State, context.Seats);
        var (rule, _) = RuleFor(context.State, context.Question.Seat);
        var (contents, invalidReason) = ParseContents(world, rule, parts);
        if (contents is null)
        {
            // 结构化取值是用户输入：不认识 / 判不了 / 组合非法都给一条可读的拒绝，而不是抛异常。
            return new SavantQuestionResolution
            {
                Ruling = SavantQuestionRuling.Invalid,
                Note = invalidReason,
            };
        }

        var fallback = $"{contents.AuditNote}；{RuleSummary(outcome, rule)}";
        var note = VortoxInterference.NoteFor(context.State, context.Question.Seat, fallback) ?? fallback;

        return new SavantQuestionResolution
        {
            Ruling = SavantQuestionRuling.Answered,
            Effective = outcome.Effective,
            Malfunctions = malfunctions,
            Note = note,
            Events =
            [
                Result(context.Question.Seat, contents.First, outcome.Effective || vortox, note),
                Result(context.Question.Seat, contents.Second, outcome.Effective || vortox, note),
            ],
        };
    }

    /// <summary>
    /// 候选事实 → 裁定点的候选项（真值、分组、事实编码、互斥组与高强度徽章随候选一起下发——
    /// 说书人端据此分栏、显示真值，并把「互为反面」的那一条**预先灰掉**）。
    /// </summary>
    private static IReadOnlyList<DecisionOption> BuildOptions(SavantFactWorld world) =>
    [
        .. SavantFactCatalog.Candidates(world).Select(candidate => new DecisionOption
        {
            Value = candidate.Value,
            Preview = candidate.Text,
            Truth = candidate.Truth,
            Group = candidate.Group,
            Code = candidate.Code,
            ExclusionGroup = candidate.ExclusionGroup,
            Tags = candidate.HighIntensity ? [SavantAccusationFacts.HighIntensityTag] : [],
        }),
    ];

    /// <summary>两条信息结果：内容只到本人；<c>MayBeFalse</c> 与注记只说书人可见（D-0012）。</summary>
    private InformationResultIssuedEvent Result(SeatId recipient, string content, bool mayBeFalse, string note) =>
        new()
        {
            Recipient = recipient,
            Ability = Ability,
            Content = content,
            MayBeFalse = mayBeFalse,
            Note = note,
        };

    /// <summary>
    /// 本次允许的真值组合（R-0057 第 5 条 / R-0028）+ 给说书人看的说明。
    /// 判不了时声明 <see cref="TruthCombinationRule.Indeterminate"/>：平台不拦提交，提交时按当时的账再判。
    /// </summary>
    private static (TruthCombinationRule Rule, string? Note) RuleFor(GameState state, SeatId seat)
    {
        if (VortoxInterference.IsActiveFor(state, seat))
        {
            return (
                TruthCombinationRule.AllFalse,
                "涡流在场：两条都必须为假，哪怕博学者醉酒或中毒"
                + "（百科《涡流》· 2026-10-01 抓取 · 角色简介 / 运作方式；R-0028）");
        }

        var outcome = state.Seat(seat) is { } entry ? AbilityEffectivenessEvaluator.Evaluate(state, entry) : null;
        return outcome switch
        {
            null => (
                TruthCombinationRule.Indeterminate,
                "账上维度还没观测齐：现在判不了能力是否生效，提交时平台按当时的账再判（不猜，D-0015）"),
            { Effective: true } => (
                TruthCombinationRule.ExactlyOneTrue,
                "能力生效：两条信息必须一真一假（百科《博学者》· 2026-10-01 抓取 · 角色能力；R-0057）"),
            _ => (
                TruthCombinationRule.AnyCombination,
                $"能力未生效（{outcome.Note ?? "原因未标注"}）：两条可以都对、都错，或一对一错"
                + "（百科《博学者》· 2026-10-01 抓取 · 角色简介；R-0057 第 5 条）"),
        };
    }

    /// <summary>结清附注里的一句话说明：这次是按哪条口径结的。</summary>
    private static string RuleSummary(AbilityOutcome outcome, TruthCombinationRule rule) => rule switch
    {
        TruthCombinationRule.AllFalse => "涡流在场：两条都必须为假（R-0028）",
        TruthCombinationRule.ExactlyOneTrue => "能力生效：两条信息一真一假，由平台按当时的账核对（R-0057）",
        TruthCombinationRule.AnyCombination =>
            $"能力未生效（{outcome.Note ?? "原因未标注"}）：两条可以都对、都错，或一对一错（R-0057 第 5 条）",
        _ => "能力是否生效判不了：平台未核对组合（D-0015）",
    };

    /// <summary>
    /// 两条信息的落地内容：玩家看到的人话 + 只进审计与说书人视图的真值记录（R-0057-C 的 C5 / C6）。
    /// </summary>
    private static (Contents? Contents, string? InvalidReason) ParseContents(
        SavantFactWorld world,
        TruthCombinationRule rule,
        string[] parts)
    {
        if (!SavantFactFormat.LooksStructured(parts[0]) || !SavantFactFormat.LooksStructured(parts[1]))
        {
            // C6：任一条是自由文本 → 不校验真假，但显式标注（老设备 / 老客户端仍然受理）。
            return (new Contents(parts[0], parts[1], "自由文本：平台不校验真假（未校验）"), null);
        }

        var first = SavantFactCatalog.Resolve(world, parts[0]);
        var second = SavantFactCatalog.Resolve(world, parts[1]);
        if (first is null || second is null)
        {
            // C5：编码外的取值，或这条事实此刻判不了（账上维度没观测齐）——不猜，显式拒绝。
            return (
                null,
                $"不认识的候选事实（或它此刻判不了）：{(first is null ? parts[0] : parts[1])}——"
                + "只能挑提示里给出的候选，或改用自由文本（平台不校验）");
        }

        var verdict = SavantFactCombination.Check(rule, first, second);
        if (!verdict.Allowed)
        {
            return (null, verdict.Reason);
        }

        return (
            new Contents(
                first.Text,
                second.Text,
                $"结构化候选（按提交时刻的账求值）：第 1 条 {first.Value} = {TruthText(first.Truth)}；"
                + $"第 2 条 {second.Value} = {TruthText(second.Truth)}"),
            null);
    }

    /// <summary>把裁定文本切成两条信息：必须恰好两段、各自非空；否则返回 null（由调用方转成显式拒绝）。</summary>
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

    private static SavantFactWorld World(GameState state, IReadOnlyList<SeatId> seats) =>
        new() { State = state, Seats = seats };

    private static string TruthText(OptionTruth truth) => truth == OptionTruth.True ? "真" : "假";

    /// <summary>两条信息的落地形态：人话文案 + 只进审计的真值记录。</summary>
    private sealed record Contents(string First, string Second, string AuditNote);
}
