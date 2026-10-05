using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 非首个夜晚获得的「首个夜晚」能力的**追加结算**（R-0055）：这类能力的行动格只出现在首夜顺序表上，
/// 其他夜晚表里没有它的位置，因此必须**追加**一格，而不是什么都不做。
/// </summary>
/// <remarks>
/// 来源（均为百科 · 2026-10-01 抓取）：《重要细节》· 七——「如果新的能力原本只在游戏的首个夜晚产生效果，
/// 那么它会在角色变化的当晚产生效果」「如果带有『在你的首个夜晚』角色在游戏中途被创造，
/// 这些角色会尽可能快地进行进场能力的效果结算……绝大部分获取信息的能力应该在所有会造成死亡的效果之后结算」；
/// 《获得能力》· 能力简介——非首个夜晚获得进场能力时「在夜晚顺序表中……插入结算这些效果」；
/// 《哲学家》· 范例 2——「在第三个夜晚，哲学家选择获得钟表匠的能力。当晚，他得知了恶魔与爪牙之间的最近的距离」；
/// 《集骨者》· 角色简介 1——「『在你的首个夜晚』或『每局游戏限一次』的能力……可以在黄昏之前再次使用」。
/// 《梦殒春宵》里「首个夜晚」且不在其他夜晚表上的角色只有钟表匠。
/// </remarks>
public sealed class GrantedEntryAbilityTests
{
    private static readonly CharacterId Philosopher = new("philosopher");
    private static readonly CharacterId BoneCollector = new("bone-collector");
    private static readonly CharacterId PitHag = new("pit-hag");
    private static readonly CharacterId Clockmaker = new("clockmaker");
    private static readonly CharacterId Dreamer = new("dreamer");

    /// <summary>哲学家第三夜获得钟表匠：追加一格给他结算（不是"本格不产生行动"）。</summary>
    [Fact]
    public void Philosopher_GrantingFirstNightAbilityOnOtherNight_AppendsSlot()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);
        var slotIndex = SlotIndex(plan, "philosopher");

        var events = PhilosopherContract().Resolve(PhilosopherContext(state, "clockmaker", plan, slotIndex));

        var inserted = Assert.Single(events.OfType<SlotInsertedEvent>());
        Assert.Equal(SlotIndex(plan, "vortox") + 1, inserted.Index);
        Assert.Equal(StepSlotKind.Action, inserted.Slot.Kind);
        Assert.Equal(new SeatId(1), inserted.Slot.Actor);
        Assert.Equal(Clockmaker, inserted.Slot.Owner);
        Assert.Equal(Philosopher, inserted.Slot.Character);
        Assert.False(inserted.Slot.Prompt!.HasOptions);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, inserted.Slot.Prompt.OnNoOption);
        Assert.Contains(
            inserted.Slot.Dependencies,
            dependency => dependency.Seat == new SeatId(1)
                && dependency.RequiredLife == LifeState.Alive
                && dependency.RequiredCharacter == Philosopher);
    }

    /// <summary>集骨者重获已死亡的钟表匠：同样追加一格（被选玩家保持死亡，依赖不锁生死）。</summary>
    [Fact]
    public void BoneCollector_RegainingFirstNightAbility_AppendsSlot()
    {
        var state = Ledger(
            (1, "clockmaker", LifeState.Dead),
            (2, "bone-collector", LifeState.Alive),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);
        var slotIndex = SlotIndex(plan, "bone-collector");

        var events = BoneCollectorContract().Resolve(
            BoneCollectorContext(state, "seat:1", plan, slotIndex));

        var inserted = Assert.Single(events.OfType<SlotInsertedEvent>());
        Assert.Equal(SlotIndex(plan, "vortox") + 1, inserted.Index);
        Assert.Equal(new SeatId(1), inserted.Slot.Actor);
        Assert.Equal(Clockmaker, inserted.Slot.Owner);
        Assert.Equal(Clockmaker, inserted.Slot.Character);
        Assert.Contains(
            inserted.Slot.Dependencies,
            dependency => dependency.Seat == new SeatId(1)
                && dependency.RequiredLife is null
                && dependency.RequiredCharacter == Clockmaker);
    }

    /// <summary>角色变更成钟表匠（游戏中途被创造）→ 同样追加（《重要细节》七）。</summary>
    [Fact]
    public void CharacterChangeIntoFirstNightAbility_AppendsSlot()
    {
        var state = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "dreamer", LifeState.Alive),
            (3, "vortox", LifeState.Alive),
            (4, "klutz", LifeState.Alive),
            (5, "artist", LifeState.Alive));
        var plan = BuildPlan(state);

        var inserted = Assert.IsType<SlotInsertedEvent>(NightSlotActivation.Plan(
            plan,
            SlotIndex(plan, "pit-hag"),
            actor: new SeatId(4),
            character: Clockmaker,
            state: state,
            lastDay: null,
            seats: [.. state.Seats.Select(entry => entry.Seat)],
            catalog: NightActions.Default));

        Assert.Equal(SlotIndex(plan, "vortox") + 1, inserted.Index);
        Assert.Equal(new SeatId(4), inserted.Slot.Actor);
        Assert.Equal(Clockmaker, inserted.Slot.Character);
    }

    /// <summary>获得发生在致死段之后（理发师换角格）→ 紧随当前格之后追加（"立即生效并进行结算"）。</summary>
    [Fact]
    public void GrantAfterTheDeathBlock_LandsRightAfterCurrentSlot()
    {
        var state = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "dreamer", LifeState.Alive),
            (3, "vortox", LifeState.Alive),
            (4, "klutz", LifeState.Alive),
            (5, "artist", LifeState.Alive));
        var plan = BuildPlan(state);
        var barberIndex = SlotIndex(plan, "barber");
        Assert.True(barberIndex > SlotIndex(plan, "vortox"));

        var inserted = Assert.IsType<SlotInsertedEvent>(NightSlotActivation.Plan(
            plan,
            barberIndex,
            actor: new SeatId(4),
            character: Clockmaker,
            state: state,
            lastDay: null,
            seats: [.. state.Seats.Select(entry => entry.Seat)],
            catalog: NightActions.Default));

        Assert.Equal(barberIndex + 1, inserted.Index);
    }

    /// <summary>首夜获得钟表匠：计划里本来就有那一格 → 走既有激活，不追加（回归）。</summary>
    [Fact]
    public void Philosopher_GrantingFirstNightAbilityOnFirstNight_ActivatesInsteadOfAppending()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state, nightNumber: 1);
        var slotIndex = SlotIndex(plan, "philosopher");

        var events = PhilosopherContract().Resolve(PhilosopherContext(state, "clockmaker", plan, slotIndex));

        Assert.Empty(events.OfType<SlotInsertedEvent>());
        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(SlotIndex(plan, "clockmaker"), activation.SlotIndex);
    }

    /// <summary>获得的是「每夜都行动」的角色（筑梦师）：本阶段有序表格 → 走既有代行，不追加（回归）。</summary>
    [Fact]
    public void Philosopher_GrantingEveryNightAbility_DoesNotAppend()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "vortox", LifeState.Alive),
            (4, "sweetheart", LifeState.Alive),
            (5, "artist", LifeState.Alive));
        var plan = BuildPlan(state);
        var slotIndex = SlotIndex(plan, "philosopher");

        var events = PhilosopherContract().Resolve(PhilosopherContext(state, "dreamer", plan, slotIndex));

        Assert.Empty(events.OfType<SlotInsertedEvent>());
        Assert.Single(events.OfType<SlotActivatedEvent>());
    }

    /// <summary>「每夜都行动」的角色的格已经走过（过时不候）：不追加（回归）。</summary>
    [Fact]
    public void PassedSlotOfAnEveryNightAbility_IsNotAppended()
    {
        var state = Ledger(
            (1, "clockmaker", LifeState.Dead),
            (2, "bone-collector", LifeState.Alive),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);

        // 筑梦师那一格在集骨者之后（还没进入）→ 走激活；这里刻意从它之后起算，模拟"时机已过"。
        Assert.Null(NightSlotActivation.PlanRegained(
            plan,
            SlotIndex(plan, "dreamer"),
            actor: new SeatId(3),
            character: Dreamer,
            state: state,
            lastDay: null,
            seats: [.. state.Seats.Select(entry => entry.Seat)],
            catalog: NightActions.Default));
    }

    /// <summary>能力未生效（酒 / 毒 / 死）：没有获得任何能力，也就不追加。</summary>
    [Fact]
    public void IneffectiveGrant_DoesNotAppend()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);
        var slotIndex = SlotIndex(plan, "philosopher");

        var events = PhilosopherContract().Resolve(
            PhilosopherContext(state, "clockmaker", plan, slotIndex, effective: false));

        Assert.Empty(events.OfType<SlotInsertedEvent>());
        Assert.Empty(events.OfType<SlotActivatedEvent>());
    }

    /// <summary>契约未实现的角色：不在夜里造提示（进格时按空槽 / 显式阻塞处理）。</summary>
    [Fact]
    public void CharacterWithoutContract_IsNotAppended()
    {
        var state = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "dreamer", LifeState.Alive),
            (3, "vortox", LifeState.Alive),
            (4, "klutz", LifeState.Alive),
            (5, "artist", LifeState.Alive));
        var plan = BuildPlan(state);

        Assert.Null(NightSlotActivation.Plan(
            plan,
            SlotIndex(plan, "pit-hag"),
            actor: new SeatId(4),
            character: new CharacterId("juggler"),
            state: state,
            lastDay: null,
            seats: [.. state.Seats.Select(entry => entry.Seat)],
            catalog: new EmptyCatalog()));
    }

    /// <summary>
    /// 同一夜两名持有者（哲学家获得 + 集骨者重获同一项「首个夜晚」能力）：**两格都追加**，
    /// 各自一格（标识带席位），后追加者先唤醒（R-0055 第 3 条的确定性口径）。
    /// </summary>
    [Fact]
    public void TwoHoldersInOneNight_BothAppended_InDocumentedOrder()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "bone-collector", LifeState.Alive),
            (3, "clockmaker", LifeState.Dead),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);

        var first = Assert.IsType<SlotInsertedEvent>(PhilosopherContract().Resolve(
            PhilosopherContext(state, "clockmaker", plan, SlotIndex(plan, "philosopher")))
            .OfType<SlotInsertedEvent>()
            .Single());

        // 把第一格折进计划（与运行时的折叠同义），再让集骨者结算。
        var folded = plan with
        {
            Slots = [.. plan.Slots.Take(first.Index), first.Slot, .. plan.Slots.Skip(first.Index)],
        };
        var second = Assert.IsType<SlotInsertedEvent>(BoneCollectorContract().Resolve(
            BoneCollectorContext(state, "seat:3", folded, SlotIndex(folded, "bone-collector")))
            .OfType<SlotInsertedEvent>()
            .Single());

        Assert.NotEqual(first.Slot.Id, second.Slot.Id);
        Assert.Equal(new SeatId(1), first.Slot.Actor);
        Assert.Equal(new SeatId(3), second.Slot.Actor);
        Assert.Equal(first.Index, second.Index);
        Assert.Equal("clockmaker@1", first.Slot.Id.Value);
        Assert.Equal("clockmaker@3", second.Slot.Id.Value);
    }

    /// <summary>
    /// 同一席位同一项能力这一夜已经追加过（二次获得 = 替换，R-0053）：不再追加第二格，
    /// 沿用已有那一格。
    /// </summary>
    [Fact]
    public void SameHolderSameAbility_IsNotAppendedTwice()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "dreamer", LifeState.Alive),
            (4, "vortox", LifeState.Alive),
            (5, "sweetheart", LifeState.Alive));
        var plan = BuildPlan(state);
        var inserted = Assert.IsType<SlotInsertedEvent>(PhilosopherContract().Resolve(
            PhilosopherContext(state, "clockmaker", plan, SlotIndex(plan, "philosopher")))
            .OfType<SlotInsertedEvent>()
            .Single());
        var folded = plan with
        {
            Slots = [.. plan.Slots.Take(inserted.Index), inserted.Slot, .. plan.Slots.Skip(inserted.Index)],
        };

        var again = PhilosopherContract().Resolve(
            PhilosopherContext(state, "clockmaker", folded, SlotIndex(folded, "philosopher")));

        Assert.Empty(again.OfType<SlotInsertedEvent>());
    }

    /// <summary>追加位不得越过黎明等待格：最后一个角色行动恰好是恶魔时，追加格落在黎明之前。</summary>
    [Fact]
    public void Insertion_NeverLandsAfterTheDawnWait()
    {
        var plan = new StepPlan
        {
            Label = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Variant = nameof(NightOrderVariant.Original),
            Slots =
            [
                StepSlot.Beat(new StepSlotId("dusk")),
                StepSlot.Action(new StepSlotId("vortox"), new SeatId(4), Prompt(), owner: new CharacterId("vortox")),
                StepSlot.DawnWait(new StepSlotId("dawn")),
            ],
        };
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (4, "vortox", LifeState.Alive));

        var inserted = Assert.IsType<SlotInsertedEvent>(NightSlotActivation.PlanGranted(
            plan,
            slotIndex: 0,
            actor: new SeatId(1),
            grantedCharacter: Clockmaker,
            state: state,
            lastDay: null,
            seats: [.. state.Seats.Select(entry => entry.Seat)],
            catalog: NightActions.Default));

        Assert.Equal(2, inserted.Index);
        Assert.Equal(StepSlotKind.DawnWait, plan.Slots[inserted.Index].Kind);
    }

    private static ChoicePrompt Prompt() => new()
    {
        Context = "测试用选择",
        Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
        OnNoOption = NoOptionBehavior.BlockAndAlert,
    };

    private static IAbilityResolution PhilosopherContract() =>
        NightActions.Resolutions.Find(Philosopher)
        ?? throw new InvalidOperationException("哲学家没有注册结算契约");

    private static IAbilityResolution BoneCollectorContract() =>
        NightActions.Resolutions.Find(BoneCollector)
        ?? throw new InvalidOperationException("集骨者没有注册结算契约");

    private static AbilityResolutionContext PhilosopherContext(
        GameState state,
        string choice,
        StepPlan plan,
        int slotIndex,
        bool effective = true) =>
        new()
        {
            SlotId = new StepSlotId("philosopher"),
            PlanLabel = plan.Label,
            Phase = plan.Phase,
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
            DaysStarted = 2,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    private static AbilityResolutionContext BoneCollectorContext(
        GameState state,
        string choice,
        StepPlan plan,
        int slotIndex) =>
        new()
        {
            SlotId = new StepSlotId("bone-collector"),
            PlanLabel = plan.Label,
            Phase = plan.Phase,
            Actor = new SeatId(2),
            ActorCharacter = BoneCollector,
            ActorOwnCharacter = BoneCollector,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome { Effective = true },
            Choice = choice,
            DaysStarted = 2,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    private static int SlotIndex(StepPlan plan, string slotId)
    {
        for (var index = 0; index < plan.Slots.Count; index++)
        {
            if (plan.Slots[index].Id.Value == slotId)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"计划里没有槽位 {slotId}");
    }

    private static StepPlan BuildPlan(GameState state, int nightNumber = 3)
    {
        var outcome = NightPlanBuilder.Build(new NightPlanRequest
        {
            NightNumber = nightNumber,
            Variant = NightOrderVariant.Original,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Actions = NightActions.Default,
        });
        Assert.Null(outcome.FailureCode);
        return Assert.IsType<StepPlan>(outcome.Plan);
    }

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        GameStateMachine.Fold(
        [
            .. rows.Select(row => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(row.Seat),
                Character = new CharacterId(row.Character),
                Alignment = Alignment.Good,
                Life = row.Life,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "测试夹具",
            }),
        ]);

    /// <summary>什么都不认的契约目录：用来覆盖「契约未实现」这一路。</summary>
    private sealed class EmptyCatalog : INightActionCatalog
    {
        public INightAction? Find(CharacterId character) => null;
    }
}
