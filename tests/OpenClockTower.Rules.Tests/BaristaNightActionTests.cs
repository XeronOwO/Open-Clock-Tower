using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 咖啡师（旅行者）的规则回归（R-0047 / R-0052）：黄昏由说书人二选一（清醒且健康 / 行动两次）、
/// 两个窗口持续到下个黄昏、受影响玩家得知是哪一个效果；「行动两次」还负责把本夜被计划判成
/// 「本夜无行动」的「每局限一次」格重新开出来（女裁缝），并对未定稿的「获得能力」显式不重开。
/// </summary>
/// <remarks>
/// 来源：百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节。
/// 通过公开目录 <see cref="NightActions"/> / <see cref="RoleContracts"/> 取契约——与运行时同一批对象。
/// </remarks>
public sealed class BaristaNightActionTests
{
    private static readonly CharacterId Barista = new("barista");

    private static IAbilityResolution Contract =>
        NightActions.Resolutions.Find(Barista)
        ?? throw new InvalidOperationException("咖啡师没有注册结算契约");

    private static INightAction PromptContract =>
        NightActions.Default.Find(Barista)
        ?? throw new InvalidOperationException("咖啡师没有注册提示契约");

    /// <summary>说书人受众：候选 = 每个在局席位 × 两个效果；生死只影响预览标注，不裁剪候选。</summary>
    [Fact]
    public void Prompt_OffersBothEffectsForEverySeat_WithLifeNotes()
    {
        var state = Ledger(
            (1, "dreamer", LifeState.Alive),
            (2, "clockmaker", LifeState.Dead),
            (3, "seamstress", LifeState.Alive),
            (4, "barista", LifeState.Alive));

        var prompt = PromptContract.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(4),
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
        });

        Assert.Equal(ChoiceAudience.Storyteller, prompt.Audience);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
        Assert.Equal(
            [
                "healthy:seat:1", "twice:seat:1",
                "healthy:seat:2", "twice:seat:2",
                "healthy:seat:3", "twice:seat:3",
                "healthy:seat:4", "twice:seat:4",
            ],
            prompt.Options.Select(option => option.Value));
        Assert.Contains(
            "已死亡",
            prompt.Options.Single(option => option.Value == "healthy:seat:2").Preview,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "已死亡",
            prompt.Options.Single(option => option.Value == "healthy:seat:1").Preview,
            StringComparison.Ordinal);
    }

    /// <summary>效果 1：落一条「清醒且健康」窗口（归因 = 咖啡师），并只把「是哪个效果」告知目标。</summary>
    [Fact]
    public void Resolve_Healthy_CreatesImmunityWindow_AndAnnouncesToTarget()
    {
        var state = Ledger((1, "dreamer", LifeState.Alive), (2, "barista", LifeState.Alive));

        var events = Contract.Resolve(Context(state, "healthy:seat:1"));

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        var effect = applied.Effect;
        Assert.Equal(new AbilityId("barista"), effect.Ability);
        Assert.Equal(new SeatId(2), effect.Source);
        Assert.Equal(new SeatId(1), effect.Target);
        Assert.Equal(Barista, effect.SourceCharacter);
        Assert.Equal(EffectWindowKind.AfflictionImmunity, effect.Window);
        Assert.Contains("sv:night-2:barista", effect.Id.Value, StringComparison.Ordinal);

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(new SeatId(1), information.Recipient);
        Assert.Contains("清醒且健康", information.Content, StringComparison.Ordinal);
        Assert.False(information.MayBeFalse);
    }

    /// <summary>效果 2：落一条「行动两次」窗口，并把当夜被计划判成「本夜无行动」的女裁缝格重开。</summary>
    [Fact]
    public void Resolve_Twice_CreatesSecondActionWindow_AndReopensUsedUpSeamstressSlot()
    {
        var state = Ledger((1, "seamstress", LifeState.Alive), (2, "barista", LifeState.Alive))
            with
        {
            AbilityUses = new AbilityUseLedger().RecordUse(
                new SeatId(1),
                new AbilityId("seamstress"),
                effective: true),
        };
        var plan = BuildPlan(state, nightNumber: 2);

        var seamstressSlot = plan.Slots.Single(slot => slot.Id.Value == "seamstress");
        Assert.Equal(StepSlotKind.Action, seamstressSlot.Kind);
        Assert.NotNull(seamstressSlot.Prompt);
        Assert.False(seamstressSlot.Prompt!.HasOptions);

        var events = Contract.Resolve(Context(state, "twice:seat:1", plan, slotIndex: 1));

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(EffectWindowKind.SecondAction, applied.Effect.Window);
        Assert.Equal(new SeatId(1), applied.Effect.Target);

        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal("seamstress", activation.SlotId.Value);
        Assert.Equal(new SeatId(1), activation.Actor);
        Assert.True(activation.Prompt.HasOptions);

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Contains("行动两次", information.Content, StringComparison.Ordinal);
    }

    /// <summary>「获得能力」的二次获得未定稿（R-0053 Open）：不重开，且跳过说明点名这件事。</summary>
    [Fact]
    public void Resolve_Twice_DoesNotReopenPhilosopherGrantSlot()
    {
        var state = WithSecondActionWindow(
            Ledger((1, "philosopher", LifeState.Alive), (2, "barista", LifeState.Alive)),
            klutz: 1,
            barista: 2) with
        {
            AbilityUses = new AbilityUseLedger().RecordUse(
                new SeatId(1),
                new AbilityId("philosopher.grant"),
                effective: false),
        };
        var plan = BuildPlan(state, nightNumber: 2);

        var slot = plan.Slots.Single(candidate => candidate.Id.Value == "philosopher");
        Assert.NotNull(slot.Prompt);
        Assert.False(slot.Prompt!.HasOptions);
        Assert.Contains("R-0053", slot.Prompt.Context, StringComparison.Ordinal);

        var events = Contract.Resolve(Context(state, "twice:seat:1", plan, slotIndex: 1));

        Assert.DoesNotContain(events, gameEvent => gameEvent is SlotActivatedEvent);
    }

    /// <summary>能力未生效（醉酒 / 中毒 / 死亡）：不落窗口、也不向目标宣告（《重要细节》三-3）。</summary>
    [Fact]
    public void Resolve_Ineffective_ProducesNothing()
    {
        var state = Ledger((1, "dreamer", LifeState.Alive), (2, "barista", LifeState.Alive));

        var events = Contract.Resolve(Context(state, "healthy:seat:1", effective: false));

        Assert.Empty(events);
    }

    /// <summary>裁定形状 / 取值 / 目标越界都显式失败，不当成「随便选一个」。</summary>
    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData("healthy:not-a-seat")]
    [InlineData("healthy:seat:99")]
    public void Resolve_InvalidDecision_Throws(string decision)
    {
        var state = Ledger((1, "dreamer", LifeState.Alive), (2, "barista", LifeState.Alive));

        Assert.Throws<InvalidOperationException>(() => Contract.Resolve(Context(state, decision)));
    }

    /// <summary>夜序：首夜与「其他夜晚」都在 Dusk 之后有咖啡师；其他夜晚它排在流莺之前（来源快照顺序）。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void NightOrder_BaristaFollowsDusk_AndPrecedesHarlotOnOtherNights(NightOrderVariant variant)
    {
        var first = NightOrderTable.For(GamePhase.FirstNight, variant);
        Assert.Equal(NightOrderEntryKind.Dusk, first[0].Kind);
        Assert.Equal(Barista, first[1].Character);

        var other = NightOrderTable.For(GamePhase.OtherNight, variant);
        Assert.Equal(NightOrderEntryKind.Dusk, other[0].Kind);
        Assert.Equal(Barista, other[1].Character);
        Assert.Equal(new CharacterId("harlot"), other[2].Character);
    }

    /// <summary>窗口收口触发器：新的一夜开始时终止上一夜的全部咖啡师窗口（标记移除，R-0052 第 4 条）。</summary>
    [Fact]
    public void WindowTrigger_TerminatesLiveWindowsAtNextNightStart()
    {
        var trigger = RoleContracts.EventTriggers.Single(candidate => candidate.Ability == new AbilityId("barista"));
        var state = Ledger((1, "dreamer", LifeState.Alive), (2, "barista", LifeState.Alive))
            with
        {
            PersistentEffects =
            [
                new PersistentEffect
                {
                    Id = new EffectId("test:barista-healthy"),
                    Source = new SeatId(2),
                    Ability = new AbilityId("barista"),
                    Target = new SeatId(1),
                    SourceCharacter = Barista,
                    Window = EffectWindowKind.AfflictionImmunity,
                },
            ],
        };

        var night = new PhaseStartedEvent
        {
            Plan = new StepPlan
            {
                Label = "sv:night-2",
                Phase = GamePhase.OtherNight,
                Slots = [StepSlot.Beat(new StepSlotId("dusk"))],
            },
            Control = ControlMode.Automatic,
        };

        var terminated = Assert.Single(
            trigger.Evaluate(ContextFor(state, events: [night])).OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(new EffectId("test:barista-healthy"), terminated.EffectId);
        Assert.Contains("R-0052", terminated.Termination!.Reason, StringComparison.Ordinal);

        // 幂等：终止事件折进账之后不再重复产出。
        var after = GameStateMachine.Apply(state, terminated);
        Assert.Empty(trigger.Evaluate(ContextFor(after, events: [night])));

        // 白天的阶段开始不动窗口。
        Assert.Empty(trigger.Evaluate(ContextFor(state, events: [new DayStartedEvent { DayNumber = 1 }])));
    }

    /// <summary>建表闸：女裁缝用过一次时，只有窗口**确认生效**才给她开格；用满两次一律不开。</summary>
    [Fact]
    public void PlanBuilder_ReopensSeamstressOnlyWithConfirmedWindow()
    {
        var used = Ledger((1, "seamstress", LifeState.Alive), (2, "barista", LifeState.Alive))
            with
        {
            AbilityUses = new AbilityUseLedger().RecordUse(
                new SeatId(1),
                new AbilityId("seamstress"),
                effective: true),
        };

        var withoutWindow = BuildPlan(used, nightNumber: 2);
        Assert.False(withoutWindow.Slots.Single(slot => slot.Id.Value == "seamstress").Prompt!.HasOptions);

        var withWindow = BuildPlan(WithSecondActionWindow(used, klutz: 1, barista: 2), nightNumber: 2);
        Assert.True(withWindow.Slots.Single(slot => slot.Id.Value == "seamstress").Prompt!.HasOptions);

        var usedTwice = used with
        {
            AbilityUses = used.AbilityUses.RecordUse(new SeatId(1), new AbilityId("seamstress"), effective: true),
        };
        var atCap = BuildPlan(WithSecondActionWindow(usedTwice, klutz: 1, barista: 2), nightNumber: 2);
        Assert.False(atCap.Slots.Single(slot => slot.Id.Value == "seamstress").Prompt!.HasOptions);
    }

    /// <summary>「必定获得正确信息」覆盖涡流（R-0047 第 4 条）：窗口生效时不落涡流失效、信息也不标可能为假。</summary>
    [Fact]
    public void VortoxInterference_IsOverriddenByImmunityWindow()
    {
        var clockmaker = NightActions.Resolutions.Find(new CharacterId("clockmaker"))!;
        var baseState = Ledger(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "vortox", LifeState.Alive));
        var immune = WithImmunityWindow(baseState, target: 1, barista: 2);

        var withWindow = ContextFor(
            immune,
            actor: new SeatId(1),
            actorCharacter: "clockmaker",
            decision: "恶魔与最近的爪牙之间隔着 1 名玩家。");

        Assert.Empty(clockmaker.InterferenceMalfunctions(withWindow));
        var information = Assert.Single(
            clockmaker.Resolve(withWindow).OfType<InformationResultIssuedEvent>());
        Assert.False(information.MayBeFalse);

        var withoutWindow = withWindow with { State = baseState };
        Assert.Equal([MalfunctionKind.Vortox], clockmaker.InterferenceMalfunctions(withoutWindow));
        Assert.True(
            Assert.Single(clockmaker.Resolve(withoutWindow).OfType<InformationResultIssuedEvent>()).MayBeFalse);
    }

    private static GameState WithImmunityWindow(GameState state, int target, int barista) =>
        state with
        {
            PersistentEffects =
            [
                new PersistentEffect
                {
                    Id = new EffectId("test:barista-healthy"),
                    Source = new SeatId(barista),
                    Ability = new AbilityId("barista"),
                    Target = new SeatId(target),
                    SourceCharacter = Barista,
                    Window = EffectWindowKind.AfflictionImmunity,
                },
            ],
        };

    private static GameState WithSecondActionWindow(GameState state, int klutz, int barista) =>
        state with
        {
            PersistentEffects =
            [
                new PersistentEffect
                {
                    Id = new EffectId("test:barista-twice"),
                    Source = new SeatId(barista),
                    Ability = new AbilityId("barista"),
                    Target = new SeatId(klutz),
                    SourceCharacter = Barista,
                    Window = EffectWindowKind.SecondAction,
                },
            ],
        };

    private static StepPlan BuildPlan(GameState state, int nightNumber)
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

    private static AbilityResolutionContext Context(
        GameState state,
        string decision,
        StepPlan? plan = null,
        int slotIndex = 0,
        bool effective = true) =>
        new()
        {
            SlotId = new StepSlotId("barista"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(2),
            ActorCharacter = Barista,
            ActorOwnCharacter = Barista,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
                Note = effective ? null : "来源中毒：能力未生效",
            },
            Decision = decision,
            DaysStarted = 1,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    private static AbilityResolutionContext ContextFor(
        GameState state,
        SeatId actor,
        string actorCharacter,
        string decision) =>
        new()
        {
            SlotId = new StepSlotId(actorCharacter),
            PlanLabel = "sv:night-1",
            Phase = GamePhase.FirstNight,
            Actor = actor,
            ActorCharacter = new CharacterId(actorCharacter),
            ActorOwnCharacter = new CharacterId(actorCharacter),
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome { Effective = true },
            Decision = decision,
            DaysStarted = 0,
        };

    private static EventTriggerContext ContextFor(GameState state, params GameEvent[] events) =>
        new()
        {
            State = state,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            Events = events,
        };

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        GameStateMachine.Fold(
        [
            .. rows.Select(row => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(row.Seat),
                Character = new CharacterId(row.Character),
                Life = row.Life,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            }),
        ]);
}
