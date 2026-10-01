using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 行动槽位的结算调度：玩家答毕 → 生效判定 → （信息类先要一次说书人裁定）→ 产出事件 → 推进。
/// 判不了（账没观测齐）整条输入被拒绝；Skip / 强推 / 作废不结算、不记「使用」。
/// </summary>
public sealed class SlotSettlementTests
{
    /// <summary>玩家答毕、额度已走完：契约产出事件，步骤机推进。</summary>
    [Fact]
    public void AnsweredRequest_ResolvesAndAdvances()
    {
        var ability = new TestAbility(produced: ProbeEffect());
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Healthy);

        var (answered, stream) = Answer(context, ability);
        Assert.Equal(StepMachineOutcomeKind.Applied, answered.Kind);

        var resolved = Assert.Single(answered.Events.OfType<AbilityResolvedEvent>());
        Assert.True(resolved.Effective);
        Assert.Equal(new SeatId(1), resolved.Actor);
        Assert.Contains(answered.Events, gameEvent => gameEvent is InstantaneousEffectAppliedEvent);
        Assert.Equal(1, ability.ResolveCalls);
        Assert.True(ability.LastEffective);
        Assert.Equal("option-a", ability.LastChoice);
        Assert.Null(ability.LastDecision);
        Assert.True(answered.State.IsPlanCompleted, "单槽位计划在结算后应已走完");
        Assert.True(StepMachineStateComparer.AreEquivalent(answered.State, StepMachine.Fold(stream)));
    }

    /// <summary>信息类：玩家选完后还要说书人裁一次；裁定前不产出结算、不推进。</summary>
    [Fact]
    public void AnsweredRequest_WithPostChoiceDecision_HoldsForStoryteller()
    {
        var ability = new TestAbility(postChoice: StepFixture.EmptyPrompt(NoOptionBehavior.StorytellerDecides));
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Healthy);

        var (answered, stream) = Answer(context, ability);

        Assert.Equal(0, ability.ResolveCalls);
        Assert.NotNull(answered.State.AwaitingDecision);
        Assert.Contains(answered.Events, gameEvent => gameEvent is DecisionPointRaisedEvent);
        Assert.False(answered.State.IsPlanCompleted);

        var decisionId = answered.State.AwaitingDecision!.Id;
        var resolvedOutcome = StepMachine.Handle(
            answered.State,
            context,
            new ResolveDecisionPointInput { DecisionPointId = decisionId, Decision = "自由裁定内容" });

        Assert.Equal(1, ability.ResolveCalls);
        Assert.Equal("option-a", ability.LastChoice);
        Assert.Equal("自由裁定内容", ability.LastDecision);
        Assert.Contains(resolvedOutcome.Events, gameEvent => gameEvent is AbilityResolvedEvent);
        Assert.True(resolvedOutcome.State.IsPlanCompleted);
        Assert.True(StepMachineStateComparer.AreEquivalent(
            resolvedOutcome.State,
            StepMachine.Fold([.. stream, .. resolvedOutcome.Events])));
    }

    /// <summary>入口裁定点（无玩家选项）：裁定即结算输入。</summary>
    [Fact]
    public void EntryDecision_ResolvesWithStorytellerDecision()
    {
        var ability = new TestAbility();
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Healthy);

        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, StepFixture.EmptyPrompt(NoOptionBehavior.StorytellerDecides), owner: "test-hero")));
        Assert.NotNull(started.State.AwaitingDecision);

        var resolvedOutcome = StepMachine.Handle(
            started.State,
            context,
            new ResolveDecisionPointInput
            {
                DecisionPointId = started.State.AwaitingDecision!.Id,
                Decision = "说书人给的数",
            });

        Assert.Equal(1, ability.ResolveCalls);
        Assert.Null(ability.LastChoice);
        Assert.Equal("说书人给的数", ability.LastDecision);
        Assert.Single(resolvedOutcome.Events.OfType<AbilityResolvedEvent>());
    }

    /// <summary>能力未生效：照常记「用过」，产出事件为零，但步骤机照走（玩家不被通知能力失败）。</summary>
    [Fact]
    public void PoisonedActor_ResolvesIneffectiveButStillRecordsUse()
    {
        var ability = new TestAbility(produced: ProbeEffect());
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Poisoned);

        var (answered, _) = Answer(context, ability);

        var resolved = Assert.Single(answered.Events.OfType<AbilityResolvedEvent>());
        Assert.False(resolved.Effective);
        Assert.Equal(MalfunctionKind.Poisoned, resolved.Malfunction);
        Assert.DoesNotContain(answered.Events, gameEvent => gameEvent is InstantaneousEffectAppliedEvent);
        Assert.False(ability.LastEffective);
        Assert.True(answered.State.IsPlanCompleted);
    }

    /// <summary>账没观测齐：整条输入被拒绝，不猜、不落事件。</summary>
    [Fact]
    public void UnobservedActor_IsRejectedWithoutGuessing()
    {
        var ability = new TestAbility();
        var emptyState = GameState.Empty;
        var context = new SettlementContext
        {
            State = emptyState,
            Seats = [new SeatId(1)],
            Abilities = new TestCatalog(ability),
        };

        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, owner: "test-hero")));
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        var answered = StepMachine.Handle(
            elapsed.State,
            context,
            new SubmitResponseInput
            {
                RequestId = started.State.PendingRequest!.Id,
                OptionValue = "option-a",
                Source = ResponseSource.Player,
            });

        Assert.Equal(StepMachineOutcomeKind.Rejected, answered.Kind);
        Assert.Equal(StepMachineRejectionReason.LedgerIncomplete, answered.RejectionReason);
        Assert.Empty(answered.Events);
        Assert.Equal(0, ability.ResolveCalls);
    }

    /// <summary>没有合法选项的 Skip：不结算、不记使用、照样推进。</summary>
    [Fact]
    public void SkippedPrompt_DoesNotSettle()
    {
        var ability = new TestAbility();
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Healthy);

        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, StepFixture.EmptyPrompt(NoOptionBehavior.Skip), owner: "test-hero")));
        Assert.Contains(started.Events, gameEvent => gameEvent is PromptSkippedEvent);

        var outcome = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());

        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is AbilityResolvedEvent);
        Assert.Equal(0, ability.ResolveCalls);
        Assert.True(outcome.State.IsPlanCompleted);
    }

    /// <summary>强推：越过挂起、不结算、不记使用（D-0014 兜底）。</summary>
    [Fact]
    public void ForceAdvance_DoesNotSettle()
    {
        var ability = new TestAbility();
        var context = Context(ability, seat: 1, DrunkState.Sober, PoisonState.Healthy);

        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, owner: "test-hero")));

        var outcome = StepMachine.Handle(
            started.State,
            context,
            new ForceAdvanceInput { Reason = "测试强推" });

        Assert.Contains(outcome.Events, gameEvent => gameEvent is SlotForceAdvancedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is AbilityResolvedEvent);
        Assert.Equal(0, ability.ResolveCalls);
        Assert.True(outcome.State.IsPlanCompleted);
    }

    /// <summary>开夜 → 配额走完 → 玩家提交；同时返回从头到尾的事件流（重放断言用）。</summary>
    private static (StepMachineOutcome Outcome, IReadOnlyList<GameEvent> Stream) Answer(
        SettlementContext context,
        IAbilityResolution ability)
    {
        var started = StepMachine.StartPhase(StepFixture.Plan(
            "test:night-1",
            StepFixture.Action("test-hero", 1, owner: ability.Character.Value)));
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        var answered = StepMachine.Handle(
            elapsed.State,
            context,
            new SubmitResponseInput
            {
                RequestId = started.State.PendingRequest!.Id,
                OptionValue = "option-a",
                Source = ResponseSource.Player,
            });

        return (answered, [.. started.Events, .. elapsed.Events, .. answered.Events]);
    }

    private static InstantaneousEffectAppliedEvent ProbeEffect() => new()
    {
        Effect = new InstantaneousEffect
        {
            Id = new EffectId("test:probe"),
            Source = new SeatId(1),
            Ability = new AbilityId("test-hero"),
            Target = new SeatId(1),
        },
    };

    private static SettlementContext Context(
        IAbilityResolution ability,
        int seat,
        DrunkState drunk,
        PoisonState poison)
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(seat),
                Character = ability.Character,
                Life = LifeState.Alive,
                Drunk = drunk,
                Poison = poison,
                Reason = "test.setup",
            },
        ]);

        return new SettlementContext
        {
            State = state,
            Seats = [new SeatId(seat)],
            Abilities = new TestCatalog(ability),
        };
    }

    private sealed class TestCatalog : IAbilityResolutionCatalog
    {
        private readonly IAbilityResolution _ability;

        internal TestCatalog(IAbilityResolution ability)
        {
            _ability = ability;
        }

        public IAbilityResolution? Find(CharacterId character) =>
            character == _ability.Character ? _ability : null;
    }

    private sealed class TestAbility : IAbilityResolution
    {
        private readonly GameEvent? _produced;

        internal TestAbility(ChoicePrompt? postChoice = null, GameEvent? produced = null)
        {
            PostChoice = postChoice;
            _produced = produced;
        }

        public CharacterId Character { get; } = new("test-hero");

        public AbilityId Ability { get; } = new("test-hero");

        internal ChoicePrompt? PostChoice { get; }

        internal int ResolveCalls { get; private set; }

        internal bool? LastEffective { get; private set; }

        internal string? LastChoice { get; private set; }

        internal string? LastDecision { get; private set; }

        public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => PostChoice;

        public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
        {
            ResolveCalls++;
            LastEffective = context.Outcome.Effective;
            LastChoice = context.Choice;
            LastDecision = context.Decision;

            // 效果类契约的义务：能力没生效就不产出任何效果事件（百科《重要细节》三-3）。
            return _produced is null || !context.Outcome.Effective ? [] : [_produced];
        }
    }
}
