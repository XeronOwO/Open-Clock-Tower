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

    /// <summary>
    /// 集骨者「重获能力」（R-0054 第 4 条）：死亡但重获能力的杂耍艺人**这一次持有重新起算**——
    /// 已经猜过一次也还能在窗口存续的这个白天再猜一次；没有窗口时按既有口径拒绝。
    /// </summary>
    [Fact]
    public void Make_AllowsDeadButRegainedJuggler_WhenAlreadyGuessedOnce()
    {
        var guessed = Make(Day(1), Context(), (2, "clockmaker")).State;

        // 先正常关掉第一天，再开第二天（白天账要求一天一天走）。
        var closed = StepMachine.Handle(guessed, Context(), new CloseDayInput());
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        var reopened = StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-2",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 2,
            previous: closed.State).State;

        Assert.Equal("juggler.not_first_day", Make(reopened, Context(), (3, "savant")).RejectionCode);

        var outcome = Make(reopened, Context(regained: true), (3, "savant"));

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var made = Assert.Single(outcome.Events.OfType<JugglerGuessesMadeEvent>());
        Assert.Equal(2, made.DayNumber);
    }

    /// <summary>
    /// 重获窗口**到期之后**（下个黄昏，窗口被终止）：起算点回到原处——窗口内猜过的那一天不算数，
    /// 但"这一次持有"已经用过，因此在窗口外的白天既不是首个白天、也没有可用的机会。
    /// </summary>
    /// <remarks>
    /// 票据 `done/bone-collector-regained-juggler-day-entry.md` 行 5：原先只有间接覆盖
    /// （"窗口不在 = 原口径"），这里把窗口**开了又关**走一遍。
    /// </remarks>
    [Fact]
    public void Make_RejectsAgainAfterTheWindowExpires()
    {
        // 第 1 天他活着猜过一次。
        var guessed = Make(Day(1), Context(), (2, "clockmaker")).State;
        var closed = StepMachine.Handle(guessed, Context(), new CloseDayInput());
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);

        // 第 2 天：窗口存续 → 受理（放宽的是起算点）。
        var reopened = StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-2",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 2,
            previous: closed.State).State;
        Assert.Equal(StepMachineOutcomeKind.Applied, Make(reopened, Context(regained: true), (2, "clockmaker")).Kind);

        // 窗口到期（下个黄昏 → DuskExpiry 终止它），第 3 天再猜：起算点回到原处。
        var closedSecond = StepMachine.Handle(reopened, ExpiredAfterRegain(), new CloseDayInput());
        Assert.Equal(StepMachineOutcomeKind.Applied, closedSecond.Kind);
        var thirdDay = StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-3",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 3,
            previous: closedSecond.State).State;

        Assert.Equal("juggler.not_first_day", Make(thirdDay, ExpiredAfterRegain(), (2, "clockmaker")).RejectionCode);
    }

    /// <summary>重获窗口内的同一天仍然只一次：放宽的是起算点，不是次数。</summary>
    [Fact]
    public void Make_StillRejectsSecondGuessInTheRegainedDay()
    {
        var first = Make(Day(1), Context(regained: true), (2, "clockmaker")).State;

        var second = Make(first, Context(regained: true), (3, "savant"));

        Assert.Equal("juggler.already_guessed", second.RejectionCode);
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
    /// <param name="regained">预置集骨者「重获能力」窗口（1 号按规则保持死亡，R-0054）。</param>
    private static SettlementContext Context(
        string character = "juggler",
        int? firstDay = 1,
        bool observed = true,
        bool regained = false) =>
        new()
        {
            State = regained
                ? GameStateMachine.Apply(Ledger(character, observed, dead: true), new PersistentEffectAppliedEvent
                {
                    Effect = RegainEffect(character),
                })
                : Ledger(character, observed),
            Seats = [Juggler, new SeatId(2), new SeatId(3)],
            Abilities = NoAbilities.Instance,
            JugglerGuesses = [new FakeSource { FirstDay = firstDay }],
        };

    /// <summary>集骨者的「重获能力」窗口效果（R-0054）：目标 = 1 号，授予角色按参数。</summary>
    private static PersistentEffect RegainEffect(string granted = "juggler") =>
        new()
        {
            Id = new EffectId("test:regain:1"),
            Source = new SeatId(2),
            Ability = new AbilityId("bone-collector.regain"),
            Target = Juggler,
            SourceCharacter = new CharacterId("bone-collector"),
            GrantedCharacter = new CharacterId(granted),
            Window = EffectWindowKind.RegainedAbility,
            SourceStateIndependent = true,
        };

    /// <summary>
    /// 「重获窗口开了又关」的账：先落窗口，再按<strong>下个黄昏</strong>的口径终止它——
    /// 窗口的寿命在规则层由 <c>DuskExpiry</c> 收口，这里照它产出的终止事件形状折叠。
    /// </summary>
    private static SettlementContext ExpiredAfterRegain()
    {
        var applied = GameStateMachine.Apply(
            Ledger("juggler", observed: true, dead: true),
            new PersistentEffectAppliedEvent { Effect = RegainEffect() });
        var expired = GameStateMachine.Apply(applied, new PersistentEffectTerminatedEvent
        {
            EffectId = new EffectId("test:regain:1"),
            Termination = new EffectTermination
            {
                Kind = EffectTerminationKind.NoLongerApplies,
                Reason = "下个黄昏：窗口到期（R-0054）",
            },
        });

        return new SettlementContext
        {
            State = expired,
            Seats = [Juggler, new SeatId(2), new SeatId(3)],
            Abilities = NoAbilities.Instance,
            JugglerGuesses = [new FakeSource()],
        };
    }

    /// <summary>基础账：1 号按参数观测到的角色与生死（集骨者只选已死亡的玩家）。</summary>
    private static GameState Ledger(string character, bool observed, bool dead = false) =>
        GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Life = dead ? LifeState.Dead : LifeState.Alive,
                Character = observed ? new CharacterId(character) : null,
                Reason = "测试夹具",
            },
        ]);

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
