using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 杂耍艺人白天公开猜测的状态机（平台口径见 <c>docs/standard/rulings.md</c> R-0057-B）：
/// 只在**首个白天**、每个首个白天一次、0–5 条、猜测公开进白天账、不在局席位与册外角色显式拒绝。
/// </summary>
/// <remarks>
/// 规则语义（谁是杂耍艺人、「首个白天」怎么起算、哪些角色名合法）由注入的
/// <see cref="IJugglerGuessSource"/> 给出——这里是内核夹具：只验证状态机形状、事件与拒绝路径；
/// 「首个白天」的真实口径由规则层测试（<c>JugglerGuessWindowTests</c>）覆盖。
/// </remarks>
public sealed class JugglerGuessMachineTests
{
    private static readonly SeatId Juggler = new(1);

    /// <summary>受理：猜测事件进流，并折进**当天账**（公开事实，所有人都看得到）。</summary>
    [Fact]
    public void Make_RecordsPublicGuessesIntoTheDayLedger()
    {
        var outcome = Make(Day(1), Context(), (2, "clockmaker"), (3, "savant"));

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var made = Assert.Single(outcome.Events.OfType<JugglerGuessesMadeEvent>());
        Assert.Equal(Juggler, made.Seat);
        Assert.Equal(1, made.DayNumber);
        Assert.Equal(2, made.Guesses.Count);

        var record = Assert.Single(outcome.State.Day!.Days[^1].JugglerGuesses);
        Assert.Equal(Juggler, record.Seat);
        Assert.Equal(1, record.DayNumber);
        Assert.Equal(new SeatId(2), record.Guesses[0].Seat);
        Assert.Equal(new CharacterId("clockmaker"), record.Guesses[0].Character);
    }

    /// <summary>0 条也受理：公开声明但不猜（规则细节 2 的场合由说书人裁量）。</summary>
    [Fact]
    public void Make_AllowsZeroGuesses()
    {
        var outcome = Make(Day(1), Context());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var made = Assert.Single(outcome.Events.OfType<JugglerGuessesMadeEvent>());
        Assert.Empty(made.Guesses);
    }

    /// <summary>超过五次：显式拒绝（百科《杂耍艺人》· 角色能力「最多五次」）。</summary>
    [Fact]
    public void Make_RejectsMoreThanFiveGuesses()
    {
        var outcome = Make(
            Day(1),
            Context(),
            (2, "clockmaker"),
            (2, "savant"),
            (2, "dreamer"),
            (2, "oracle"),
            (2, "sage"),
            (2, "artist"));

        Assert.Equal("juggler.too_many_guesses", outcome.RejectionCode);
        Assert.Empty(outcome.State.Day!.Days[^1].JugglerGuesses);
    }

    /// <summary>每个首个白天一次：同一天第二次被拒（不产生第二条公开事实）。</summary>
    [Fact]
    public void Make_RejectsSecondGuessInTheSameDay()
    {
        var first = Make(Day(1), Context(), (2, "clockmaker")).State;

        var second = Make(first, Context(), (3, "savant"));

        Assert.Equal("juggler.already_guessed", second.RejectionCode);
        Assert.Empty(second.Events);
    }

    /// <summary>只在首个白天：第二天再来就被拒（即使他从没猜过；平台按账起算）。</summary>
    [Fact]
    public void Make_RejectsOutsideTheFirstDay()
    {
        // 先正常关掉第一天，再开第二天（白天账要求一天一天走）。
        var closed = StepMachine.Handle(Day(1), Context(), new CloseDayInput());
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);

        var second = StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-2",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 2,
            previous: closed.State).State;

        var outcome = Make(second, Context(firstDay: 1), (2, "clockmaker"));

        Assert.Equal("juggler.not_first_day", outcome.RejectionCode);
        Assert.Empty(outcome.Events);
    }

    /// <summary>不是白天：夜晚提交被阶段闸拦下（白天输入的公共前置）。</summary>
    [Fact]
    public void Make_RejectsOutsideDay()
    {
        var night = StepMachine.StartPhase(
            new StepPlan
            {
                Label = "sv:night-1",
                Phase = GamePhase.FirstNight,
                Slots = [StepFixture.Beat("dusk")],
            },
            ControlMode.Automatic).State;

        Assert.Equal("day.not_open", Make(night, Context(), (2, "clockmaker")).RejectionCode);
    }

    /// <summary>该席位不是杂耍艺人：找不到猜测来源契约，显式拒绝。</summary>
    [Fact]
    public void Make_RejectsNonJugglerSeat()
    {
        var outcome = Make(Day(1), Context(character: "clockmaker"), (2, "savant"));

        Assert.Equal("juggler.not_juggler", outcome.RejectionCode);
    }

    /// <summary>角色维度还没观测：判不了是不是杂耍艺人 → 拒绝（不猜，D-0015）。</summary>
    [Fact]
    public void Make_RejectsUnobservedCharacter()
    {
        var outcome = Make(Day(1), Context(observed: false), (2, "clockmaker"));

        Assert.Equal("juggler.character_unobserved", outcome.RejectionCode);
    }

    /// <summary>「首个白天」起算说不清（规则层判不了）：拒绝而不是默认放行。</summary>
    [Fact]
    public void Make_RejectsWhenTheTenureIsUnknown()
    {
        var outcome = Make(Day(1), Context(firstDay: null), (2, "clockmaker"));

        Assert.Equal("juggler.tenure_unknown", outcome.RejectionCode);
    }

    /// <summary>猜不在局座次里的席位：拒绝（离场 / 不存在的席位都不是"任意玩家"）。</summary>
    [Fact]
    public void Make_RejectsGuessAboutASeatOutsideTheGame()
    {
        var outcome = Make(Day(1), Context(), (9, "clockmaker"));

        Assert.Equal("juggler.guess_unknown_seat", outcome.RejectionCode);
    }

    /// <summary>猜册外的角色名：拒绝（打错字不是策略）。</summary>
    [Fact]
    public void Make_RejectsUnknownCharacterName()
    {
        var outcome = Make(Day(1), Context(), (2, "not-a-character"));

        Assert.Equal("juggler.guess_unknown_character", outcome.RejectionCode);
    }

    private static StepMachineOutcome Make(
        StepMachineState state,
        SettlementContext context,
        params (int Seat, string Character)[] guesses) =>
        StepMachine.Handle(
            state,
            context,
            new MakeJugglerGuessesInput
            {
                Seat = Juggler,
                Guesses =
                [
                    .. guesses.Select(guess => new JugglerGuess
                    {
                        Seat = new SeatId(guess.Seat),
                        Character = new CharacterId(guess.Character),
                    }),
                ],
            });

    /// <summary>白天步骤机状态（唯一 DayWindow 槽位，与生产一致）。</summary>
    private static StepMachineState Day(int dayNumber) =>
        StepMachine.StartDay(
            new StepPlan
            {
                Label = $"sv:day-{dayNumber}",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber).State;

    /// <summary>结算上下文：1 号是指定角色；席位 1 / 2 / 3 在局。</summary>
    private static SettlementContext Context(
        string character = "juggler",
        int? firstDay = 1,
        bool observed = true) =>
        new()
        {
            State = GameStateMachine.Fold(
            [
                new SeatStateChangedEvent
                {
                    Seat = Juggler,
                    Life = LifeState.Alive,
                    Character = observed ? new CharacterId(character) : null,
                    Reason = "测试夹具",
                },
            ]),
            Seats = [Juggler, new SeatId(2), new SeatId(3)],
            Abilities = NoAbilities.Instance,
            JugglerGuesses = [new FakeSource { FirstDay = firstDay }],
        };

    /// <summary>测试用猜测来源：首个白天固定（null = 判不了）；角色名只认两个。</summary>
    private sealed class FakeSource : IJugglerGuessSource
    {
        internal int? FirstDay { get; init; } = 1;

        public CharacterId Character => new("juggler");

        public int? FirstHeldDay(GameState state, SeatId seat) => FirstDay;

        public bool IsKnownCharacter(CharacterId character) =>
            character.Value is "clockmaker" or "savant" or "dreamer" or "oracle" or "sage" or "artist";
    }

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
