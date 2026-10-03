using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 艺术家的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0040）：
/// 四种回答选项；三种回答消耗能力并下发信息（未生效也算），「要求重问」不消耗。
/// </summary>
/// <remarks>来源：百科《艺术家》· 2026-10-01 抓取 · 规则细节 1 / 角色简介 / 运作方式 1–11。</remarks>
public sealed class ArtistQuestionSourceTests
{
    private static readonly SeatId Artist = new(1);

    /// <summary>裁定点：四种回答；「要求重问」必须与三种回答同列（R-0040 第 4 条）。</summary>
    [Fact]
    public void Prompt_OffersFourAnswers()
    {
        var prompt = Source().BuildPrompt("2 号是爪牙吗？");

        Assert.Equal(
            [
                "yes",
                "no",
                "unknown",
                "retry",
            ],
            prompt.Options.Select(option => option.Value));
        Assert.Contains("2 号是爪牙吗？", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>要求重问：不消耗、不下发信息（问题不合规时玩家不该被消耗）。</summary>
    [Fact]
    public void Resolve_Returned_DoesNotConsume()
    {
        var resolution = Source().Resolve(
            Context(Ledger(), "retry"));

        Assert.NotNull(resolution);
        Assert.Equal(ArtistQuestionRuling.Returned, resolution!.Ruling);
        Assert.Empty(resolution.Events);
        Assert.NotNull(resolution.Note);
    }

    /// <summary>「是」：信息只到本人，内容与回答一致。</summary>
    [Fact]
    public void Resolve_Yes_IssuesInformation()
    {
        var resolution = Source().Resolve(Context(Ledger(), "yes"));

        Assert.NotNull(resolution);
        Assert.Equal(ArtistQuestionRuling.Answered, resolution!.Ruling);
        Assert.True(resolution.Effective);
        var information = Assert.Single(resolution.Events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(Artist, information.Recipient);
        Assert.Equal("是", information.Content);
        Assert.False(information.MayBeFalse);
    }

    /// <summary>「我不知道」同样是一次有效询问：消耗能力、内容照发。</summary>
    [Fact]
    public void Resolve_Unknown_IssuesInformation()
    {
        var resolution = Source().Resolve(
            Context(Ledger(), "unknown"));

        Assert.NotNull(resolution);
        Assert.Equal(ArtistQuestionRuling.Answered, resolution!.Ruling);
        Assert.Equal("我不知道", Assert.Single(resolution.Events.OfType<InformationResultIssuedEvent>()).Content);
    }

    /// <summary>醉酒 / 中毒：能力不生效——回答照发、标「可能为假」，使用仍被浪费（三-3）。</summary>
    [Fact]
    public void Resolve_Ineffective_MarksMayBeFalse()
    {
        var resolution = Source().Resolve(
            Context(Ledger(artistDrunk: true), "no"));

        Assert.NotNull(resolution);
        Assert.False(resolution!.Effective);
        Assert.Contains(MalfunctionKind.Drunk, resolution.Malfunctions);
        var information = Assert.Single(resolution.Events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("醉酒", information.Note!, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：艺术家的信息必须为假（R-0028 / R-0004：干扰分类进失效账本）。</summary>
    [Fact]
    public void Resolve_WithVortox_MarksInterference()
    {
        var resolution = Source().Resolve(
            Context(Ledger(vortoxAlive: true), "yes"));

        Assert.NotNull(resolution);
        Assert.Contains(MalfunctionKind.Vortox, resolution!.Malfunctions);
        var information = Assert.Single(resolution.Events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("必须为假", information.Note!, StringComparison.Ordinal);
    }

    /// <summary>生死 / 醉酒 / 中毒没观测齐：判不了就返回 null，由内核显式拒绝（不猜，D-0015）。</summary>
    [Fact]
    public void Resolve_UnobservedDimensions_ReturnsNull()
    {
        var resolution = Source().Resolve(
            Context(Ledger(unobserved: true), "yes"));

        Assert.Null(resolution);
    }

    /// <summary>非法裁定值：显式抛错（拒绝由内核的合法性判定给出，这里防事件流损坏）。</summary>
    [Fact]
    public void Resolve_InvalidDecision_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Source().Resolve(Context(Ledger(), "maybe")));
    }

    /// <summary>从规则层注册表取艺术家提问来源（契约实现对测试保持内部）。</summary>
    private static IArtistQuestionSource Source() =>
        RoleContracts.ArtistQuestions.Single(source => source.Character == new CharacterId("artist"));

    private static ArtistQuestionResolutionContext Context(GameState state, string decision) => new()
    {
        Question = new ArtistQuestion
        {
            Seat = Artist,
            Character = new CharacterId("artist"),
            Question = "2 号是爪牙吗？",
        },
        Decision = decision,
        State = state,
        Seats = [.. state.Seats.Select(entry => entry.Seat)],
    };

    /// <summary>1 号是艺术家；可选叠加醉酒 / 涡流存活 / 维度未观测。</summary>
    private static GameState Ledger(
        bool artistDrunk = false,
        bool vortoxAlive = false,
        bool unobserved = false)
    {
        var seats = new List<SeatStateEntry>
        {
            new()
            {
                Seat = Artist,
                Character = Fact(new CharacterId("artist")),
                Life = Fact(LifeState.Alive),
                Drunk = unobserved ? null : Fact(artistDrunk ? DrunkState.Drunk : DrunkState.Sober),
                Poison = unobserved ? null : Fact(PoisonState.Healthy),
            },
        };

        if (vortoxAlive)
        {
            seats.Add(new SeatStateEntry
            {
                Seat = new SeatId(2),
                Character = Fact(new CharacterId("vortox")),
                Life = Fact(LifeState.Alive),
            });
        }

        return new GameState { Seats = seats };
    }

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
