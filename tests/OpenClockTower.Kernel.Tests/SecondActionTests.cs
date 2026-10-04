using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 咖啡师「行动两次」的步骤机口径（R-0052 第 2 / 3 条）：窗口生效且本格确实结算过时，
/// **同一槽位**重进一次（请求 / 裁定标识带遍次），而不是推进到下一格；跳过 / 作废 / 强推不重进；
/// 「每局限一次」的能力以总使用次数 &lt; 2 为上限。
/// </summary>
public sealed class SecondActionTests
{
    private static readonly SeatId Actor = new(1);
    private static readonly SeatId Barista = new(9);

    /// <summary>窗口生效：第一次结算后重进本格（新请求带 #2），第二遍结算后才推进。</summary>
    [Fact]
    public void ResolvedActionSlot_WithWindow_ReentersOnceThenAdvances()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);

        var started = StepMachine.StartPhase(Plan());
        var firstRequest = started.State.PendingRequest!;
        Assert.Equal("test:night-1:test-hero", firstRequest.Id.Value);
        Assert.Equal(1, started.State.SlotPass);

        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        var answered = StepMachine.Handle(elapsed.State, context, Answer(firstRequest));

        Assert.Equal(StepMachineOutcomeKind.Applied, answered.Kind);
        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotEnteredEvent entered && entered.SlotIndex == 0);
        Assert.DoesNotContain(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(0, answered.State.SlotIndex);
        Assert.Equal(2, answered.State.SlotPass);
        Assert.False(answered.State.IsPlanCompleted);
        Assert.Equal(1, ability.ResolveCalls);
        Assert.Equal("test:night-1:test-hero#2", answered.State.PendingRequest!.Id.Value);

        // 第二遍有自己的配额：到点后答毕才推进。
        var elapsedSecond = StepMachine.Handle(answered.State, context, new SlotQuotaElapsedInput());
        var second = StepMachine.Handle(elapsedSecond.State, context, Answer(elapsedSecond.State.PendingRequest!));

        Assert.Equal(1, second.State.SlotIndex);
        Assert.Equal(1, second.State.SlotPass);
        Assert.Equal(2, ability.ResolveCalls);
        Assert.DoesNotContain(
            second.Events,
            gameEvent => gameEvent is SlotEnteredEvent entered && entered.SlotIndex == 0);
        Assert.Single(second.Events.OfType<AbilityResolvedEvent>());
    }

    /// <summary>对照：没有窗口时答完即推进（行为与咖啡师引入前一致）。</summary>
    [Fact]
    public void ResolvedActionSlot_WithoutWindow_Advances()
    {
        var ability = new TestAbility();
        var context = Context(ability);

        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        var answered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(1, answered.State.SlotIndex);
        Assert.Equal(1, ability.ResolveCalls);
    }

    /// <summary>窗口在别的席位身上：本格不重进（窗口不跨席位生效）。</summary>
    [Fact]
    public void WindowOnAnotherSeat_DoesNotReenter()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true, windowTarget: new SeatId(2));
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var answered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(1, answered.State.SlotIndex);
    }

    /// <summary>契约声明不支持二次结算（集骨者「用后即失去自身能力」，R-0054 第 5 条）：不重进、不静默加戏。</summary>
    [Fact]
    public void ContractRefusingSecondAction_DoesNotReenter()
    {
        var ability = new TestAbility(supportsSecondAction: false);
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var answered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(1, ability.ResolveCalls);
    }

    /// <summary>「每局限一次」：还没用满两次时窗口给第二遍；第二遍结算后到顶，不再重进。</summary>
    [Fact]
    public void LimitedPerGame_ReentersUntilTwoUses()
    {
        var ability = new TestAbility(limitedPerGame: true);
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var first = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Equal("test:night-1:test-hero#2", first.State.PendingRequest!.Id.Value);

        var elapsedSecond = StepMachine.Handle(first.State, context, new SlotQuotaElapsedInput());
        var second = StepMachine.Handle(
            elapsedSecond.State,
            context,
            Answer(elapsedSecond.State.PendingRequest!));

        Assert.Equal(1, second.State.SlotIndex);
        Assert.Equal(2, ability.ResolveCalls);
    }

    /// <summary>「每局限一次」已经用过一次：窗口只还一笔——第一遍结算后总次数到 2，不再重进。</summary>
    [Fact]
    public void LimitedPerGame_AtTwoUses_DoesNotReenter()
    {
        var ability = new TestAbility(limitedPerGame: true);
        var context = Context(ability, window: true, seededUses: 1);
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var answered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(1, answered.State.SlotIndex);
    }

    /// <summary>秒回（配额还没走完）：先不重进也不推进；配额到点才重进（不能被"答得快"吞掉）。</summary>
    [Fact]
    public void EarlyAnswer_ReentersOnlyWhenQuotaElapses()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());

        var answered = StepMachine.Handle(started.State, context, Answer(started.State.PendingRequest!));

        Assert.Equal(0, answered.State.SlotIndex);
        Assert.Equal(1, answered.State.SlotPass);
        Assert.Equal(OperationRequestStatus.Answered, answered.State.PendingRequest!.Status);
        Assert.DoesNotContain(answered.Events, gameEvent => gameEvent is SlotEnteredEvent);

        var elapsed = StepMachine.Handle(answered.State, context, new SlotQuotaElapsedInput());

        Assert.Equal(2, elapsed.State.SlotPass);
        Assert.Equal(0, elapsed.State.SlotIndex);
        Assert.Equal("test:night-1:test-hero#2", elapsed.State.PendingRequest!.Id.Value);
    }

    /// <summary>没有行动就重进（无合法选项的 Skip 槽位）：没有能力可再结算一次。</summary>
    [Fact]
    public void SkippedPrompt_DoesNotReenter()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, StepFixture.EmptyPrompt(NoOptionBehavior.Skip), owner: "test-hero"),
            StepFixture.Beat("beat-1")));

        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        Assert.Contains(elapsed.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.DoesNotContain(
            elapsed.Events,
            gameEvent => gameEvent is SlotEnteredEvent entered && entered.SlotIndex == 0);
        Assert.Equal(1, elapsed.State.SlotIndex);
        Assert.Equal(0, ability.ResolveCalls);
    }

    /// <summary>请求被作废：没有能力结算，不重进。</summary>
    [Fact]
    public void VoidedRequest_DoesNotReenter()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());

        var voided = StepMachine.Handle(
            started.State,
            context,
            new VoidRequestInput
            {
                RequestId = started.State.PendingRequest!.Id,
                Reason = OperationRequestVoidReason.StorytellerTakeover,
                Note = "测试作废",
            });
        var elapsed = StepMachine.Handle(voided.State, context, new SlotQuotaElapsedInput());

        Assert.Contains(elapsed.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Equal(0, ability.ResolveCalls);
    }

    /// <summary>强推越过第二遍：说书人兜底入口永远开着，重进被跳过（D-0014）。</summary>
    [Fact]
    public void ForceAdvance_DuringSecondPass_SkipsIt()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        var reentered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        var forced = StepMachine.Handle(reentered.State, context, new ForceAdvanceInput { Reason = "测试强推" });

        Assert.Contains(forced.Events, gameEvent => gameEvent is SlotForceAdvancedEvent);
        Assert.Equal(1, forced.State.SlotIndex);
        Assert.Equal(1, ability.ResolveCalls);
    }

    /// <summary>入口裁定点的第二次进入：裁定标识带遍次（与请求标识同一口径）。</summary>
    [Fact]
    public void EntryDecision_SecondPassCarriesSuffixedIdentifier()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, StepFixture.EmptyPrompt(NoOptionBehavior.StorytellerDecides), owner: "test-hero"),
            StepFixture.Beat("beat-1")));

        Assert.Equal("test:night-1:test-hero:decision", started.State.AwaitingDecision!.Id.Value);

        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        var resolved = StepMachine.Handle(
            elapsed.State,
            context,
            new ResolveDecisionPointInput
            {
                DecisionPointId = started.State.AwaitingDecision!.Id,
                Decision = "第二遍见",
            });

        Assert.Equal("test:night-1:test-hero#2:decision", resolved.State.AwaitingDecision!.Id.Value);
        Assert.Equal(2, resolved.State.SlotPass);
    }

    /// <summary>
    /// 第一遍的效果让行动者站不住（这里：自己死了）：第二遍不重开，记一条跳过并直接推进
    /// （与入槽检查同一把尺子，口径更保守——第一遍确实已经落地）。
    /// </summary>
    [Fact]
    public void Reentry_AfterFirstPassKillsActor_SkipsSecondPass()
    {
        var ability = new TestAbility(produced: new SeatStateChangedEvent
        {
            Seat = Actor,
            Life = LifeState.Dead,
            Reason = "测试：第一遍把自己杀了",
        });
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(Plan());
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var answered = StepMachine.Handle(elapsed.State, context, Answer(started.State.PendingRequest!));

        Assert.Contains(answered.Events, gameEvent => gameEvent is SlotAdvancedEvent);
        Assert.Contains(answered.Events, gameEvent => gameEvent is PromptSkippedEvent);
        Assert.Null(answered.State.PendingRequest);
        Assert.Equal(1, answered.State.SlotIndex);
        Assert.Equal(1, ability.ResolveCalls);
    }

    /// <summary>使用次数与「是否生效」无关（R-0052 第 3 条按总次数判）。</summary>
    [Fact]
    public void UseCount_CountsIneffectiveUsesToo()
    {
        var ledger = new AbilityUseLedger()
            .RecordUse(Actor, new AbilityId("test-hero"), effective: false)
            .RecordUse(Actor, new AbilityId("test-hero"), effective: true)
            .RecordUse(new SeatId(2), new AbilityId("test-hero"), effective: true);

        Assert.True(ledger.WasUsed(Actor, new AbilityId("test-hero")));
        Assert.True(ledger.WasEffective(Actor, new AbilityId("test-hero")));
        Assert.Equal(2, ledger.UseCount(Actor, new AbilityId("test-hero")));
    }

    /// <summary>受众原语：说书人受众 → 裁定点（选项是结构化候选）；行动者受众 → 操作请求。</summary>
    [Fact]
    public void ChoicePrompt_AudienceDecidesTheProjection()
    {
        var storyteller = new ChoicePrompt
        {
            Context = "说书人二选一",
            Options = [new DecisionOption { Value = "healthy:seat:1", Preview = "1 号：清醒且健康" }],
            Audience = ChoiceAudience.Storyteller,
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
        var actor = storyteller with { Audience = ChoiceAudience.Actor };

        Assert.Equal(DecisionPointOutcome.StorytellerDecides, storyteller.Evaluate());
        Assert.Equal(DecisionPointOutcome.AwaitingChoice, actor.Evaluate());
    }

    /// <summary>说书人受众的槽位在入槽时开裁定点（而不是给行动者发请求）。</summary>
    [Fact]
    public void StorytellerAudienceSlot_RaisesDecisionPoint()
    {
        var ability = new TestAbility();
        var context = Context(ability, window: true);
        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, new ChoicePrompt
            {
                Context = "说书人二选一",
                Options = [new DecisionOption { Value = "healthy:seat:1", Preview = "1 号：清醒且健康" }],
                Audience = ChoiceAudience.Storyteller,
                OnNoOption = NoOptionBehavior.StorytellerDecides,
            }, owner: "test-hero"),
            StepFixture.Beat("beat-1")));

        Assert.Null(started.State.PendingRequest);
        Assert.Equal("test:night-1:test-hero:decision", started.State.AwaitingDecision!.Id.Value);
        Assert.Single(started.State.AwaitingDecision.Prompt.Options);

        // 结清后照常结算（与行动者受众同一路径）。
        var resolved = StepMachine.Handle(
            started.State,
            context,
            new ResolveDecisionPointInput
            {
                DecisionPointId = started.State.AwaitingDecision!.Id,
                Decision = "healthy:seat:1",
            });
        Assert.Equal(1, ability.ResolveCalls);
        Assert.Equal("healthy:seat:1", ability.LastDecision);
    }

    private static StepPlan Plan() => StepFixture.Plan(
        "test:night-1",
        StepFixture.Action("test-hero", 1, owner: "test-hero"),
        StepFixture.Beat("beat-1"));

    private static SubmitResponseInput Answer(OperationRequest request) => new()
    {
        RequestId = request.Id,
        OptionValue = "option-a",
        Source = ResponseSource.Player,
    };

    private static SettlementContext Context(
        TestAbility ability,
        bool window = false,
        SeatId? windowTarget = null,
        int seededUses = 0)
    {
        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                Seat = Actor,
                Character = ability.Character,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
            new SeatStateChangedEvent
            {
                Seat = Barista,
                Character = new CharacterId("barista"),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
        };

        for (var index = 0; index < seededUses; index++)
        {
            events.Add(new AbilityResolvedEvent
            {
                SlotId = new StepSlotId($"seed-{index}"),
                Actor = Actor,
                Ability = ability.Ability,
                Effective = true,
            });
        }

        if (window)
        {
            events.Add(new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId("test:barista-twice"),
                    Source = Barista,
                    Ability = new AbilityId("barista"),
                    Target = windowTarget ?? Actor,
                    SourceCharacter = new CharacterId("barista"),
                    Window = EffectWindowKind.SecondAction,
                },
            });
        }

        return new SettlementContext
        {
            State = GameStateMachine.Fold(events),
            Seats = [Actor, Barista],
            Abilities = new TestCatalog(ability),
        };
    }

    private sealed class TestCatalog : IAbilityResolutionCatalog
    {
        private readonly TestAbility _ability;

        internal TestCatalog(TestAbility ability)
        {
            _ability = ability;
        }

        public IAbilityResolution? Find(CharacterId character) =>
            character == _ability.Character ? _ability : null;
    }

    private sealed class TestAbility : IAbilityResolution
    {
        private readonly GameEvent? _produced;

        internal TestAbility(
            bool supportsSecondAction = true,
            bool limitedPerGame = false,
            GameEvent? produced = null)
        {
            SupportsSecondAction = supportsSecondAction;
            IsLimitedPerGame = limitedPerGame;
            _produced = produced;
        }

        public CharacterId Character { get; } = new("test-hero");

        public AbilityId Ability { get; } = new("test-hero");

        public bool SupportsSecondAction { get; }

        public bool IsLimitedPerGame { get; }

        internal int ResolveCalls { get; private set; }

        internal string? LastDecision { get; private set; }

        public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

        public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
        {
            ResolveCalls++;
            LastDecision = context.Decision;
            return _produced is null ? [] : [_produced];
        }
    }
}
