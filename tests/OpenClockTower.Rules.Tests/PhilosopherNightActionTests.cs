using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 哲学家的规则回归：每局限一次获得一名镇民 / 外来者角色的能力（不变身）、摇头可不用、
/// 获得能力的落格（当夜在被获得角色的格上代行 / 那一格有人时归它自己）。
/// </summary>
/// <remarks>
/// 来源：百科《哲学家》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0036。
/// </remarks>
public sealed class PhilosopherNightActionTests
{
    private static readonly CharacterId Philosopher = new("philosopher");

    /// <summary>提示：镇民 / 外来者角色（按花名册顺序）+ 摇头；不含哲学家自己，也不含爪牙 / 恶魔。</summary>
    [Fact]
    public void Prompt_OffersGoodCharactersAndDecline()
    {
        var prompt = Prompt();
        var values = prompt.Options.Select(option => option.Value).ToArray();

        Assert.Contains("dreamer", values);
        Assert.Contains("klutz", values);
        Assert.DoesNotContain("philosopher", values);
        Assert.DoesNotContain("vortox", values);
        Assert.DoesNotContain("pit-hag", values);
        Assert.Equal("decline", values[^1]);
    }

    /// <summary>摇头：什么都不发生，之后的夜里还可以再选（「每局限一次」约束的是"获得"）。</summary>
    [Fact]
    public void Decline_DoesNothing()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker"));

        var events = Contract().Resolve(Context(state, "decline"));

        Assert.Empty(events);
    }

    /// <summary>
    /// 获得能力：落一条常驻标记效果（带被获得的角色），**不写角色维度**（不变身），
    /// 也不产生任何状态变化。
    /// </summary>
    [Fact]
    public void Grant_RecordsMarkerEffectWithoutChangingCharacter()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker"));

        var events = Contract().Resolve(Context(state, "dreamer"));

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(new EffectId("philosopher.grant:1"), applied.Effect.Id);
        Assert.Equal(new SeatId(1), applied.Effect.Source);
        Assert.Equal(new SeatId(1), applied.Effect.Target);
        Assert.Equal(new AbilityId("philosopher.grant"), applied.Effect.Ability);
        Assert.Equal(new CharacterId("philosopher"), applied.Effect.SourceCharacter);
        Assert.Equal(new CharacterId("dreamer"), applied.Effect.GrantedCharacter);
        Assert.Null(applied.Effect.Dimension);
        Assert.Empty(events.OfType<SeatStateChangedEvent>());
        Assert.Empty(events.OfType<SlotActivatedEvent>());
    }

    /// <summary>
    /// 被获得角色的格今夜还没进入、且没有行动者 → 当夜就把那一格交给获得者代行（首夜能力照此落地）。
    /// </summary>
    [Fact]
    public void Grant_ActivatesGrantedSlotWhenFreeAndAhead()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker"));
        var plan = OtherNightPlan(dreamerActor: null);

        var events = Contract().Resolve(Context(state, "dreamer", plan: plan, slotIndex: 1));

        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(2, activation.SlotIndex);
        Assert.Equal(new StepSlotId("dreamer"), activation.SlotId);
        Assert.Equal(new SeatId(1), activation.Actor);
        Assert.Equal(Philosopher, activation.ActorCharacter);
        Assert.Equal(Philosopher, activation.Dependencies[0].RequiredCharacter);
    }

    /// <summary>被获得角色的格已经有行动者（角色在场且存活）→ 不动它：持有者照常被唤醒（醉酒 → 不生效）。</summary>
    [Fact]
    public void Grant_DoesNotTakeOverSlotWithLivingHolder()
    {
        var state = NightLedger((1, "philosopher"), (2, "dreamer"));
        var plan = OtherNightPlan(dreamerActor: 2);

        var events = Contract().Resolve(Context(state, "dreamer", plan: plan, slotIndex: 1));

        Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Empty(events.OfType<SlotActivatedEvent>());
    }

    /// <summary>能力不生效（中毒 / 醉酒 / 死亡）：不获得能力——照常选完，但什么都不发生。</summary>
    [Fact]
    public void IneffectiveAbility_DoesNothing()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker"));

        var events = Contract().Resolve(Context(state, "dreamer", effective: false));

        Assert.Empty(events);
    }

    /// <summary>已经获得过能力，却又开出第二次选择 → 事件流损坏，显式抛错。</summary>
    [Fact]
    public void SecondGrant_Throws()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker")) with
        {
            PersistentEffects = [GrantEffect(seat: 1, granted: new CharacterId("dreamer"))],
        };

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "klutz")));
    }

    /// <summary>选择不在「镇民 / 外来者」可选集里 → 显式抛错，不静默当成摇头。</summary>
    [Fact]
    public void InvalidChoice_Throws()
    {
        var state = NightLedger((1, "philosopher"), (2, "clockmaker"));

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "vortox")));
    }

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(Philosopher)
        ?? throw new InvalidOperationException("哲学家没有注册结算契约");

    private static ChoicePrompt Prompt() =>
        (NightActions.Default.Find(Philosopher)
            ?? throw new InvalidOperationException("哲学家没有注册提示契约")).BuildPrompt(new NightActionContext
            {
                Actor = new SeatId(1),
                Seats = [new SeatId(1), new SeatId(2)],
                State = GameState.Empty,
            });

    internal static PersistentEffect GrantEffect(int seat, CharacterId granted) => new()
    {
        Id = new EffectId($"philosopher.grant:{seat}"),
        Source = new SeatId(seat),
        Ability = new AbilityId("philosopher.grant"),
        Target = new SeatId(seat),
        SourceCharacter = Philosopher,
        GrantedCharacter = granted,
    };

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        StepPlan? plan = null,
        int slotIndex = 0) => new()
        {
            SlotId = new StepSlotId("philosopher"),
            PlanLabel = "sv:night-1",
            Phase = GamePhase.FirstNight,
            Actor = new SeatId(1),
            ActorCharacter = Philosopher,
            ActorOwnCharacter = Philosopher,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
            },
            Choice = choice,
            DaysStarted = 0,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    /// <summary>首夜风格的计划：节拍 → 哲学的格 → 筑梦师的格（是否已有行动者由参数决定）。</summary>
    private static StepPlan OtherNightPlan(int? dreamerActor) => new()
    {
        Label = "sv:night-1",
        Phase = GamePhase.FirstNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Action(
                new StepSlotId("philosopher"),
                new SeatId(1),
                Prompt(),
                owner: Philosopher),
            dreamerActor is { } actor
                ? StepSlot.Action(new StepSlotId("dreamer"), new SeatId(actor), Prompt(), owner: new CharacterId("dreamer"))
                : StepSlot.Empty(new StepSlotId("dreamer"), new CharacterId("dreamer")),
        ],
    };

    private static GameState NightLedger(params (int Seat, string Character)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(Alignment.Good),
                    Life = Fact(LifeState.Alive),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
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
