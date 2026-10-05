using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 博学者提问来源的规则回归（R-0057）：提示无选项、两条信息按 <c>|</c> 切分且各发一条信息结果、
/// 格式不合显式 Invalid（不抛异常）、能力未生效与涡流在场时的失效分类与说明、维度没观测齐返回 null。
/// </summary>
/// <remarks>
/// 来源：百科《博学者》· 2026-10-01 抓取 · 角色能力 / 角色简介。
/// </remarks>
public sealed class SavantQuestionSourceTests
{
    private static readonly SeatId Savant = new(1);

    /// <summary>提示：没有选项（两条信息由说书人自由填写），并把分隔符写清楚。</summary>
    [Fact]
    public void Prompt_HasNoOptions_AndExplainsTheSeparator()
    {
        var prompt = Source().BuildPrompt();

        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Contains("|", prompt.Context, StringComparison.Ordinal);
        Assert.Contains("不判定真假", prompt.Context, StringComparison.Ordinal);
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

    private static SavantQuestionResolutionContext Context(GameState state, string decision) => new()
    {
        Question = new SavantQuestion { Seat = Savant, Character = new CharacterId("savant") },
        Decision = decision,
        State = state,
        Seats = [Savant, new SeatId(2), new SeatId(3), new SeatId(4), new SeatId(5)],
    };

    /// <summary>五席的账：1 号博学者，4 号涡流（需要时），其余镇民。</summary>
    private static GameState Ledger(bool poisoned = false, bool vortox = false, bool unobservedDimensions = false)
    {
        var rows = new (int Seat, string Character)[]
        {
            (1, "savant"),
            (2, "clockmaker"),
            (3, "dreamer"),
            (4, vortox ? "vortox" : "artist"),
            (5, "klutz"),
        };

        return GameStateMachine.Fold(
        [
            .. rows.Select(row => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(row.Seat),
                Character = new CharacterId(row.Character),
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
