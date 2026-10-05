using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 博学者提问来源的规则回归（R-0057 / R-0057-C）：提示摆出候选事实与真值并声明本次组合、
/// 两条信息按 <c>|</c> 切分且各发一条信息结果、结构化路径按账求值与核对组合、
/// 自由文本路径照旧受理但标「未校验」、格式不合与非法组合显式 Invalid（不抛异常）、
/// 能力未生效与涡流在场时的口径、维度没观测齐返回 null。
/// </summary>
/// <remarks>
/// 来源：百科《博学者》· 2026-10-01 抓取 · 角色能力 / 角色简介；《涡流》· 2026-10-01 抓取 · 角色简介。
/// </remarks>
public sealed class SavantQuestionSourceTests
{
    private static readonly SeatId Savant = new(1);

    /// <summary>提示：摆出候选事实与真值、两个槽位同源，并声明本次允许的组合。</summary>
    [Fact]
    public void Prompt_OffersTruthCandidates_AndDeclaresTheRule()
    {
        var prompt = Source().BuildPrompt(PromptContext(Ledger()));

        Assert.NotEmpty(prompt.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Equal(ChoiceAudience.Storyteller, prompt.Audience);
        Assert.Equal(TruthCombinationRule.ExactlyOneTrue, prompt.TruthRule);
        Assert.Contains("一真一假", prompt.TruthNote!, StringComparison.Ordinal);
        Assert.Contains("|", prompt.Context, StringComparison.Ordinal);
        Assert.Contains("不校验真假", prompt.Context, StringComparison.Ordinal);
        Assert.All(prompt.Options, option =>
        {
            Assert.NotNull(option.Truth);
            Assert.NotNull(option.Group);
            Assert.StartsWith("fact:", option.Value, StringComparison.Ordinal);
        });

        // 两个槽位从同一个候选集合里挑（答案形状 = 第一条|第二条，见 ChoicePrompt.IsLegalAnswer）。
        Assert.True(prompt.IsLegalAnswer($"{prompt.Options[0].Value}|{prompt.Options[1].Value}"));
    }

    /// <summary>能力未生效（中毒）：组合改成「任意」——两条可以都对、都错，或一对一错（R-0057 第 5 条）。</summary>
    [Fact]
    public void Prompt_Ineffective_AllowsAnyCombination()
    {
        var prompt = Source().BuildPrompt(PromptContext(Ledger(poisoned: true)));

        Assert.Equal(TruthCombinationRule.AnyCombination, prompt.TruthRule);
        Assert.Contains("一对一错", prompt.TruthNote!, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：组合改成「两条都必须为假」（R-0028）。</summary>
    [Fact]
    public void Prompt_Vortox_RequiresBothFalse()
    {
        var prompt = Source().BuildPrompt(PromptContext(Ledger(vortox: true)));

        Assert.Equal(TruthCombinationRule.AllFalse, prompt.TruthRule);
        Assert.Contains("涡流", prompt.TruthNote!, StringComparison.Ordinal);
    }

    /// <summary>维度没观测齐：声明「判不了」，但候选照给——提交时按当时的账再判（不猜，D-0015）。</summary>
    [Fact]
    public void Prompt_Indeterminate_DeclaresNothingButStillOffersCandidates()
    {
        var prompt = Source().BuildPrompt(PromptContext(Ledger(unobservedDimensions: true)));

        Assert.Equal(TruthCombinationRule.Indeterminate, prompt.TruthRule);
        Assert.NotEmpty(prompt.Options);
        Assert.Contains("判不了", prompt.TruthNote!, StringComparison.Ordinal);
    }

    /// <summary>正常路径：两条各发一条信息结果，收件人只有本人，两条都标「可能为假」。</summary>
    [Fact]
    public void Resolve_IssuesTwoResultsToTheSavantOnly()
    {
        var resolution = Source().Resolve(Context(Ledger(), "3 号是镇民|5 号是爪牙"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Answered, resolution!.Ruling);
        Assert.True(resolution.Effective);
        Assert.Empty(resolution.Malfunctions);

        var results = resolution.Events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal("3 号是镇民", results[0].Content);
        Assert.Equal("5 号是爪牙", results[1].Content);
        Assert.All(results, result =>
        {
            Assert.Equal(Savant, result.Recipient);
            Assert.True(result.MayBeFalse);
        });
        Assert.Contains("一真一假", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 结构化路径（R-0057-C）：玩家收到的是**人话文案**（不是编码），
    /// 审计与说书人视图的注记里留着编码与各自真值。
    /// </summary>
    [Fact]
    public void Resolve_StructuredPair_SendsPlainLanguageAndRecordsTruths()
    {
        var resolution = Source().Resolve(
            Context(Ledger(), "fact:seat-character:2:clockmaker|fact:seat-is-evil:2"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Answered, resolution!.Ruling);

        var results = resolution.Events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal("2 号玩家的角色是「钟表匠」", results[0].Content);
        Assert.Equal("2 号玩家是邪恶阵营", results[1].Content);
        Assert.Contains("fact:seat-character:2:clockmaker", resolution.Note!, StringComparison.Ordinal);
        Assert.Contains("= 真", resolution.Note!, StringComparison.Ordinal);
        Assert.Contains("= 假", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>C1：能力生效时两条都为真 → 显式拒绝并说明原因（不落任何信息）。</summary>
    [Fact]
    public void Resolve_BothTrue_IsRejected()
    {
        var resolution = Source().Resolve(
            Context(Ledger(), "fact:seat-character:2:clockmaker|fact:role-in-play:clockmaker"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Invalid, resolution!.Ruling);
        Assert.Contains("一真一假", resolution.Note!, StringComparison.Ordinal);
        Assert.Empty(resolution.Events);
    }

    /// <summary>C3：涡流在场时两条都必须为假；夹一条真话就被拒。</summary>
    [Fact]
    public void Resolve_VortoxWithATrueStatement_IsRejected()
    {
        var resolution = Source().Resolve(
            Context(Ledger(vortox: true), "fact:seat-character:2:clockmaker|fact:seat-is-evil:2"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Invalid, resolution!.Ruling);
        Assert.Contains("涡流", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>C3：涡流在场时两条假话受理；未生效的分类与说明照旧（R-0028 / R-0004）。</summary>
    [Fact]
    public void Resolve_VortoxWithTwoFalseStatements_IsAccepted()
    {
        var resolution = Source().Resolve(
            Context(Ledger(vortox: true), "fact:seat-is-evil:2|fact:role-in-play:juggler"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Answered, resolution!.Ruling);
        Assert.Contains(MalfunctionKind.Vortox, resolution.Malfunctions);
        Assert.Contains("涡流", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>C4：同一条事实写两遍 / 两条互为反面 → 拒绝（平台防呆）。</summary>
    [Theory]
    [InlineData("fact:seat-is-evil:2|fact:seat-is-evil:2")]
    [InlineData("fact:demon-seat-parity:odd|fact:demon-seat-parity:even")]
    public void Resolve_SameOrOppositeFacts_AreRejected(string decision)
    {
        var resolution = Source().Resolve(Context(Ledger(), decision));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Invalid, resolution!.Ruling);
        Assert.Contains("防呆", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>C5：编码外的取值 / 参数不认识 / 此刻判不了 → 可读拒绝（不猜）。</summary>
    [Theory]
    [InlineData("fact:no-such-fact|fact:seat-is-evil:2")]
    [InlineData("fact:seat-is-evil:2|fact:seat-is-evil:99")]
    [InlineData("fact:|fact:seat-is-evil:2")]
    public void Resolve_UnknownStructuredValue_IsRejected(string decision)
    {
        var resolution = Source().Resolve(Context(Ledger(), decision));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Invalid, resolution!.Ruling);
        Assert.Empty(resolution.Events);
    }

    /// <summary>C6：任一条是自由文本 → 不校验真假，但显式标注「未校验」（R-0057 第 3 条仍然受理）。</summary>
    [Fact]
    public void Resolve_MixedStructuredAndFreeText_IsAcceptedButUnverified()
    {
        var resolution = Source().Resolve(Context(Ledger(), "fact:seat-is-evil:2|说书人自己写的一条"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Answered, resolution!.Ruling);

        var results = resolution.Events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal("fact:seat-is-evil:2", results[0].Content);
        Assert.Equal("说书人自己写的一条", results[1].Content);
        Assert.Contains("未校验", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>两条信息两侧的空白被去掉（说书人手写的文本不该把空格带进玩家的信息里）。</summary>
    [Fact]
    public void Resolve_TrimsBothParts()
    {
        var resolution = Source().Resolve(Context(Ledger(), "  3 号是镇民 | 5 号是爪牙  "));

        var results = resolution!.Events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal("3 号是镇民", results[0].Content);
        Assert.Equal("5 号是爪牙", results[1].Content);
    }

    /// <summary>格式不合（没有分隔符 / 分成三条 / 有一条是空的）：给 Invalid 而不是抛异常。</summary>
    [Theory]
    [InlineData("只有一条")]
    [InlineData("甲|乙|丙")]
    [InlineData("甲|")]
    [InlineData("|乙")]
    [InlineData("|")]
    public void Resolve_InvalidFormat_IsReportedAsInvalid(string decision)
    {
        var resolution = Source().Resolve(Context(Ledger(), decision));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Invalid, resolution!.Ruling);
        Assert.Contains("|", resolution.Note!, StringComparison.Ordinal);
        Assert.Empty(resolution.Events);
    }

    /// <summary>能力未生效（中毒）：照常给两条，但带失效分类与说明（《重要细节》三-3）。</summary>
    [Fact]
    public void Resolve_Poisoned_KeepsResultsWithMalfunction()
    {
        var resolution = Source().Resolve(Context(Ledger(poisoned: true), "甲|乙"));

        Assert.NotNull(resolution);
        Assert.False(resolution!.Effective);
        Assert.Contains(MalfunctionKind.Poisoned, resolution.Malfunctions);
        Assert.Equal(2, resolution.Events.OfType<InformationResultIssuedEvent>().Count());
        Assert.Contains("中毒", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>能力未生效时"两条都对"也受理：任意组合都可以（R-0057 第 5 条）。</summary>
    [Fact]
    public void Resolve_Ineffective_AcceptsTwoTrueStatements()
    {
        var resolution = Source().Resolve(
            Context(Ledger(poisoned: true), "fact:seat-character:2:clockmaker|fact:role-in-play:clockmaker"));

        Assert.NotNull(resolution);
        Assert.Equal(SavantQuestionRuling.Answered, resolution!.Ruling);
    }

    /// <summary>涡流在场：两条都必须为假——加失效分类并改说明（R-0028 / R-0004）。</summary>
    [Fact]
    public void Resolve_Vortox_MarksTheInterference()
    {
        var resolution = Source().Resolve(Context(Ledger(vortox: true), "甲|乙"));

        Assert.NotNull(resolution);
        Assert.Contains(MalfunctionKind.Vortox, resolution!.Malfunctions);
        Assert.Contains("涡流", resolution.Note!, StringComparison.Ordinal);
    }

    /// <summary>席位的醉酒 / 中毒还没观测齐：判不了 → null（由内核显式拒绝，不猜）。</summary>
    [Fact]
    public void Resolve_Indeterminate_ReturnsNull()
    {
        Assert.Null(Source().Resolve(Context(Ledger(unobservedDimensions: true), "甲|乙")));
    }

    private static ISavantQuestionSource Source() =>
        RoleContracts.SavantQuestions.Single(source => source.Character == new CharacterId("savant"));

    private static SavantPromptContext PromptContext(GameState state) => new()
    {
        Seat = Savant,
        State = state,
        Seats = Seats(),
    };

    private static SavantQuestionResolutionContext Context(GameState state, string decision) => new()
    {
        Question = new SavantQuestion { Seat = Savant, Character = new CharacterId("savant") },
        Decision = decision,
        State = state,
        Seats = Seats(),
    };

    private static IReadOnlyList<SeatId> Seats() =>
        [Savant, new SeatId(2), new SeatId(3), new SeatId(4), new SeatId(5)];

    /// <summary>五席的账：1 号博学者，4 号恶魔（涡流或诺-达鲺），其余镇民 / 外来者；阵营齐全。</summary>
    private static GameState Ledger(bool poisoned = false, bool vortox = false, bool unobservedDimensions = false)
    {
        var rows = new (int Seat, string Character, Alignment Alignment)[]
        {
            (1, "savant", Alignment.Good),
            (2, "clockmaker", Alignment.Good),
            (3, "dreamer", Alignment.Good),
            (4, vortox ? "vortox" : "no-dashii", Alignment.Evil),
            (5, "klutz", Alignment.Good),
        };

        return GameStateMachine.Fold(
        [
            .. rows.Select(row => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(row.Seat),
                Character = new CharacterId(row.Character),
                Alignment = row.Alignment,
                Life = LifeState.Alive,
                Drunk = unobservedDimensions && row.Seat == 1 ? null : DrunkState.Sober,
                Poison = unobservedDimensions && row.Seat == 1
                    ? null
                    : poisoned && row.Seat == 1 ? PoisonState.Poisoned : PoisonState.Healthy,
                Reason = "测试夹具",
            }),
        ]);
    }
}
