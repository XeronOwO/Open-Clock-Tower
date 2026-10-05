using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 杂耍艺人的夜间信息（R-0057-B）：只有昨天白天做过公开猜测才唤醒；说书人比划**猜对数**，
/// 信息只到本人；推演值按结算时刻的角色快照给（判定与生效同一时刻）。
/// </summary>
/// <remarks>
/// 依据：百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力 / 角色简介 3 / 运作方式 5。
/// </remarks>
public sealed class JugglerNightActionTests
{
    private static readonly SeatId Juggler = new(1);

    /// <summary>昨天没有做出公开猜测：槽位照走，但不唤醒（空选项 + Skip，R-0009）。</summary>
    [Fact]
    public void Prompt_WithoutGuesses_Skips()
    {
        var prompt = Action().BuildPrompt(Context(lastDay: null));

        Assert.Equal(NoOptionBehavior.Skip, prompt.OnNoOption);
        Assert.Empty(prompt.Options);
        Assert.Contains("没有做出公开猜测", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>昨天猜过：提示里带上平台按结算时刻快照算出的猜对数（数字仍由说书人给）。</summary>
    [Fact]
    public void Prompt_WithGuesses_ShowsThePlatformCount()
    {
        var prompt = Action().BuildPrompt(Context(lastDay: Day((2, "clockmaker"), (3, "artist"), (2, "sage"))));

        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Contains("猜对 1 条", prompt.Context, StringComparison.Ordinal);
        Assert.Contains("共提交 3 条", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：提示里写明这条信息必须为假（R-0028）。</summary>
    [Fact]
    public void Prompt_WithVortox_SaysTheNumberMustBeFalse()
    {
        var prompt = Action().BuildPrompt(Context(lastDay: Day((2, "clockmaker")), vortox: true));

        Assert.Contains("涡流在场", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>结清：一条信息只发给本人；未生效 / 涡流时标「可能为假」并带说明。</summary>
    [Fact]
    public void Resolve_IssuesTheNumberToTheJugglerOnly()
    {
        var events = Resolve(Day((2, "clockmaker"), (3, "artist")), decision: "1");

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(Juggler, information.Recipient);
        Assert.Equal("1", information.Content);
        Assert.False(information.MayBeFalse);
        Assert.Contains("猜对 1 条", information.Note!, StringComparison.Ordinal);
    }

    /// <summary>能力未生效（醉酒 / 中毒）：信息照发但标「可能为假」，说明沿用失效原因。</summary>
    [Fact]
    public void Resolve_Ineffective_MarksMayBeFalse()
    {
        var events = Resolve(
            Day((2, "clockmaker")),
            decision: "0",
            effective: false,
            malfunctionNote: "来源中毒：能力未生效");

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("中毒", information.Note!, StringComparison.Ordinal);
    }

    /// <summary>被猜席位的角色没观测齐：提示明说算不出来（不猜，D-0015）。</summary>
    [Fact]
    public void Prompt_WithUnobservedCharacter_SaysItCannotCount()
    {
        var state = State(withSeatTwo: false);
        var prompt = Action().BuildPrompt(new NightActionContext
        {
            Actor = Juggler,
            Seats = Seats(),
            State = state,
            LastDay = Day((2, "clockmaker")),
        });

        Assert.Contains("算不出猜对数", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>0 条猜测（公开声明但不猜）：照样唤醒，推演 0。</summary>
    [Fact]
    public void Prompt_WithAnEmptyDeclaration_CountsZero()
    {
        var prompt = Action().BuildPrompt(Context(lastDay: Day()));

        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Contains("猜对 0 条", prompt.Context, StringComparison.Ordinal);
    }

    private static INightAction Action() =>
        NightActions.Default.Find(new CharacterId("juggler"))
        ?? throw new InvalidOperationException("杂耍艺人没有登记夜间提示契约");

    private static IAbilityResolution Resolution() =>
        NightActions.Resolutions.Find(new CharacterId("juggler"))
        ?? throw new InvalidOperationException("杂耍艺人没有登记结算契约");

    private static NightActionContext Context(DayRecord? lastDay, bool vortox = false) => new()
    {
        Actor = Juggler,
        Seats = Seats(),
        State = State(vortox: vortox),
        LastDay = lastDay,
    };

    private static IReadOnlyList<GameEvent> Resolve(
        DayRecord? lastDay,
        string decision,
        bool effective = true,
        string? malfunctionNote = null) =>
        Resolution().Resolve(new AbilityResolutionContext
        {
            SlotId = new StepSlotId("juggler"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = Juggler,
            ActorCharacter = new CharacterId("juggler"),
            ActorOwnCharacter = new CharacterId("juggler"),
            Seats = Seats(),
            State = State(),
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
                Note = malfunctionNote,
            },
            Decision = decision,
            DaysStarted = 1,
            LastDay = lastDay,
        });

    private static IReadOnlyList<SeatId> Seats() => [Juggler, new SeatId(2), new SeatId(3)];

    /// <summary>三席账：1 号杂耍艺人、2 号钟表匠、3 号贤者（需要时给涡流）。</summary>
    private static GameState State(bool vortox = false, bool withSeatTwo = true) =>
        GameStateMachine.Fold(
        [
            Row(1, "juggler"),
            Row(2, withSeatTwo ? "clockmaker" : null),
            Row(3, vortox ? "vortox" : "sage"),
        ]);

    private static SeatStateChangedEvent Row(int seat, string? character) => new()
    {
        Seat = new SeatId(seat),
        Life = LifeState.Alive,
        Character = character is null ? null : new CharacterId(character),
        Alignment = seat == 3 && character == "vortox" ? Alignment.Evil : Alignment.Good,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "测试夹具",
    };

    /// <summary>昨天白天的账：这个席位公开猜了哪几条。</summary>
    private static DayRecord Day(params (int Seat, string Character)[] guesses) => new()
    {
        DayNumber = 1,
        Status = DayStatus.Closed,
        JugglerGuesses =
        [
            new JugglerGuessRecord
            {
                Seat = Juggler,
                DayNumber = 1,
                Guesses =
                [
                    .. guesses.Select(guess => new JugglerGuess
                    {
                        Seat = new SeatId(guess.Seat),
                        Character = new CharacterId(guess.Character),
                    }),
                ],
            },
        ],
    };
}
