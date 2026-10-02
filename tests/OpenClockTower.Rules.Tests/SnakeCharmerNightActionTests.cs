using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 舞蛇人角色变更的规则回归：只选存活玩家；选中恶魔时交换角色与阵营；原恶魔永久中毒
/// （来源状态无关）；换手后恶魔槽位重绑给新持有者；非恶魔无事发生。
/// 来源：百科《舞蛇人》· 2026-10-01 抓取 · 角色能力 / 运作方式 / 提示标记；
/// 《重要细节》三-1（可选自己）；《夜晚行动顺序一览》· 2026-10-01 抓取 · 舞蛇人条；
/// 平台口径 <c>docs/standard/rulings.md</c> R-0031 / R-0032。
/// </summary>
public sealed class SnakeCharmerNightActionTests
{
    private static readonly SeatId CharmerSeat = new(1);
    private static readonly SeatId DemonSeat = new(2);

    /// <summary>提示只列存活席位（含自己，百科《重要细节》三-1），不列死者。</summary>
    [Fact]
    public void Prompt_OffersOnlyLivingSeats()
    {
        var state = WithLife(
            Ledger(
                (1, "snake-charmer", Alignment.Good),
                (2, "vortox", Alignment.Evil),
                (3, "clockmaker", Alignment.Good)),
            seat: 3,
            LifeState.Dead);

        var prompt = PromptContract().BuildPrompt(new NightActionContext
        {
            Actor = CharmerSeat,
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
            State = state,
        });

        Assert.Equal(new[] { "seat:1", "seat:2" }, prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>有席位的生死未观测 → 不猜「存活玩家」集合，显式阻塞（R-0009 / D-0015）。</summary>
    [Fact]
    public void Prompt_UnobservedLife_Blocks()
    {
        var state = Ledger((1, "snake-charmer", Alignment.Good), (2, "vortox", Alignment.Evil));
        var unobserved = state with
        {
            Seats = [.. state.Seats.Select(entry => entry.Seat == DemonSeat ? entry with { Life = null } : entry)],
        };

        var prompt = PromptContract().BuildPrompt(new NightActionContext
        {
            Actor = CharmerSeat,
            Seats = [CharmerSeat, DemonSeat],
            State = unobserved,
        });

        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.BlockAndAlert, prompt.OnNoOption);
    }

    /// <summary>选中恶魔 → 双方交换角色与阵营；原恶魔获得永久中毒；恶魔槽位重绑给新恶魔。</summary>
    [Fact]
    public void ChoosesDemon_SwapsCharactersAndAlignments_AndPoisonsFormerDemon()
    {
        var state = Ledger(
            (1, "snake-charmer", Alignment.Good),
            (2, "vortox", Alignment.Evil),
            (3, "clockmaker", Alignment.Good));
        var plan = OtherNightPlan();

        var events = Contract().Resolve(Context(state, "seat:2", plan: plan, slotIndex: 1));

        var changes = events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, changes.Length);

        var charmer = changes.Single(change => change.Seat == CharmerSeat);
        Assert.Equal(new CharacterId("vortox"), charmer.Character);
        Assert.Equal(Alignment.Evil, charmer.Alignment);
        Assert.Equal(CharmerSeat, charmer.CausedBy);

        var formerDemon = changes.Single(change => change.Seat == DemonSeat);
        Assert.Equal(new CharacterId("snake-charmer"), formerDemon.Character);
        Assert.Equal(Alignment.Good, formerDemon.Alignment);
        Assert.Equal(CharmerSeat, formerDemon.CausedBy);

        var poison = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(DemonSeat, poison.Effect.Source);
        Assert.Equal(DemonSeat, poison.Effect.Target);
        Assert.Equal(EffectDimension.Poison, poison.Effect.Dimension);
        Assert.Equal(new CharacterId("snake-charmer"), poison.Effect.SourceCharacter);
        Assert.Equal(new AbilityId("snake-charmer.poison"), poison.Effect.Ability);
        Assert.True(poison.Effect.SourceStateIndependent);

        var rebound = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(2, rebound.SlotIndex);
        Assert.Equal(CharmerSeat, rebound.Actor);
    }

    /// <summary>
    /// 特殊情形：邪恶舞蛇人 × 善良恶魔 → 依旧交换阵营（运作方式：「在舞蛇人属于邪恶阵营，
    /// 或者恶魔属于善良阵营的特殊情况下，依旧相应地交换他们的阵营」）。
    /// </summary>
    [Fact]
    public void EvilCharmerAndGoodDemon_SwapAlignmentsAsWell()
    {
        var state = Ledger((1, "snake-charmer", Alignment.Evil), (2, "vortox", Alignment.Good));

        var events = Contract().Resolve(Context(state, "seat:2"));

        var changes = events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(Alignment.Good, changes.Single(change => change.Seat == CharmerSeat).Alignment);
        Assert.Equal(Alignment.Evil, changes.Single(change => change.Seat == DemonSeat).Alignment);
    }

    /// <summary>选中非恶魔 / 选自己 → 无事发生（能力照常记「已使用且生效」，不是「未生效」）。</summary>
    [Theory]
    [InlineData("seat:2")]
    [InlineData("seat:1")]
    public void ChoosesNonDemon_DoesNothing(string choice)
    {
        var state = Ledger((1, "snake-charmer", Alignment.Good), (2, "clockmaker", Alignment.Good));

        var events = Contract().Resolve(Context(state, choice));

        Assert.Empty(events);
    }

    /// <summary>中毒 / 醉酒 / 死亡 → 不交换、不落中毒，也不允许伪造角色或阵营变化。</summary>
    [Fact]
    public void IneffectiveAbility_DoesNothing()
    {
        var state = Ledger((1, "snake-charmer", Alignment.Good), (2, "vortox", Alignment.Evil));

        var events = Contract().Resolve(Context(state, "seat:2", effective: false));

        Assert.Empty(events);
    }

    /// <summary>请求挂起期间目标死亡 → 答案失效、整条命令失败（请求保持挂起，玩家可重选）。</summary>
    [Fact]
    public void TargetDiedWhilePending_Throws()
    {
        var state = WithLife(
            Ledger((1, "snake-charmer", Alignment.Good), (2, "vortox", Alignment.Evil)),
            seat: 2,
            LifeState.Dead);

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "seat:2")));
    }

    /// <summary>首夜没有恶魔行动槽位 → 只交换、不产重绑（没有可绑的格，过时不候）。</summary>
    [Fact]
    public void FirstNightWithoutDemonSlot_DoesNotRebind()
    {
        var state = Ledger((1, "snake-charmer", Alignment.Good), (2, "vortox", Alignment.Evil));
        var plan = new StepPlan
        {
            Label = "sv:night-1",
            Phase = GamePhase.FirstNight,
            Slots =
            [
                StepSlot.Beat(new StepSlotId("dusk")),
                StepSlot.Action(
                    new StepSlotId("snake-charmer"),
                    CharmerSeat,
                    Prompt(),
                    owner: new CharacterId("snake-charmer")),
            ],
        };

        var events = Contract().Resolve(Context(
            state,
            "seat:2",
            plan: plan,
            slotIndex: 1,
            phase: GamePhase.FirstNight));

        Assert.Equal(2, events.OfType<SeatStateChangedEvent>().Count());
        Assert.Empty(events.OfType<SlotActivatedEvent>());
    }

    private static INightAction PromptContract() =>
        NightActions.Default.Find(new CharacterId("snake-charmer"))
        ?? throw new InvalidOperationException("舞蛇人提示契约未注册进 NightActions");

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(new CharacterId("snake-charmer"))
        ?? throw new InvalidOperationException("舞蛇人结算契约未注册进 NightActions");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        StepPlan? plan = null,
        int slotIndex = 0,
        GamePhase phase = GamePhase.OtherNight) => new()
        {
            SlotId = new StepSlotId("snake-charmer"),
            PlanLabel = "sv:night-2",
            Phase = phase,
            Actor = CharmerSeat,
            ActorCharacter = new CharacterId("snake-charmer"),
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunction = effective ? null : MalfunctionKind.Poisoned,
            },
            Choice = choice,
            DaysStarted = 1,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    /// <summary>其他夜晚的计划：节拍 → 舞蛇人 → 涡流（涡流槽位建表时绑给 2 号）。</summary>
    private static StepPlan OtherNightPlan() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Action(
                new StepSlotId("snake-charmer"),
                CharmerSeat,
                Prompt(),
                owner: new CharacterId("snake-charmer")),
            StepSlot.Action(
                new StepSlotId("vortox"),
                DemonSeat,
                Prompt(),
                owner: new CharacterId("vortox")),
        ],
    };

    private static ChoicePrompt Prompt() => new()
    {
        Context = "测试用选择",
        Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
        OnNoOption = NoOptionBehavior.BlockAndAlert,
    };

    private static GameState Ledger(params (int Seat, string Character, Alignment Alignment)[] rows) => new()
    {
        Seats =
        [
            .. rows.Select(row => new SeatStateEntry
            {
                Seat = new SeatId(row.Seat),
                Character = Fact(new CharacterId(row.Character)),
                Alignment = Fact(row.Alignment),
                Life = Fact(LifeState.Alive),
                Drunk = Fact(DrunkState.Sober),
                Poison = Fact(PoisonState.Healthy),
            }),
        ],
    };

    private static GameState WithLife(GameState state, int seat, LifeState life) => state with
    {
        Seats =
        [
            .. state.Seats.Select(entry => entry.Seat == new SeatId(seat) ? entry with { Life = Fact(life) } : entry),
        ],
    };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
