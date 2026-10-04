using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 集骨者「重获能力」窗口的账本语义（R-0054）：死者身上有生效中的重获窗口时按「仍握有角色能力」处理
/// （生效判定 / 效果生效 / 入槽放行的共用口径）；窗口终止（下个黄昏 / 来源死亡 / 离场）时，被重获
/// 能力名下在这段窗口里产生的持续型效果与疯狂要求一并终止——既成事实类（SourceStateIndependent）
/// 效果不回溯（心上人醉酒，R-0039）。
/// </summary>
public sealed class RegainedAbilityTests
{
    private static readonly SeatId Collector = new(1);
    private static readonly SeatId Target = new(2);
    private static readonly SeatId Victim = new(3);

    private static readonly PersistentEffect Regain = new()
    {
        Id = new EffectId("test:regain:2"),
        Source = Collector,
        Ability = new AbilityId("bone-collector.regain"),
        Target = Target,
        SourceCharacter = new CharacterId("bone-collector"),
        GrantedCharacter = new CharacterId("dreamer"),
        Window = EffectWindowKind.RegainedAbility,
        SourceStateIndependent = true,
    };

    private static readonly PersistentEffect Dependent = new()
    {
        Id = new EffectId("test:dependent"),
        Source = Target,
        Ability = new AbilityId("dreamer"),
        Target = Victim,
        SourceCharacter = new CharacterId("dreamer"),
    };

    /// <summary>能力存续查询：存活 → true；死亡 → 看有没有生效中的重获窗口。</summary>
    [Fact]
    public void AbilityPresentOn_TracksTheRegainWindow()
    {
        var dead = Baseline();
        Assert.False(dead.AbilityPresentOn(Target));

        var regained = GameStateMachine.Apply(dead, Apply(Regain));
        Assert.True(regained.AbilityPresentOn(Target));
        Assert.True(regained.RegainedAbilityOn(Target));

        var afterDusk = ApplyTermination(regained, Regain);
        Assert.False(afterDusk.AbilityPresentOn(Target));
    }

    /// <summary>死者名下非「与来源状态无关」的效果：没有重获窗口时不生效，窗口生效时生效。</summary>
    [Fact]
    public void IsOperative_DeadSource_RequiresTheRegainWindow()
    {
        var without = GameStateMachine.Apply(Baseline(), Apply(Dependent));
        Assert.False(without.IsOperative(Dependent));

        var with = GameStateMachine.Apply(
            GameStateMachine.Apply(Baseline(), Apply(Dependent)),
            Apply(Regain));
        Assert.True(with.IsOperative(Dependent));
    }

    /// <summary>生效判定：死亡 + 重获窗口按「握有角色能力」放行；醉酒 / 中毒照旧使其失效。</summary>
    [Fact]
    public void Effectiveness_DeadButRegained_ActsAndStillRespectsPoison()
    {
        var regained = GameStateMachine.Apply(Baseline(), Apply(Regain));

        var outcome = AbilityEffectivenessEvaluator.Evaluate(regained, regained.Seat(Target)!);
        Assert.NotNull(outcome);
        Assert.True(outcome!.Effective);

        var poisoned = GameStateMachine.Apply(regained, new SeatStateChangedEvent
        {
            Seat = Target,
            Poison = PoisonState.Poisoned,
            Reason = "测试：重获后中毒",
        });
        var poisonedOutcome = AbilityEffectivenessEvaluator.Evaluate(poisoned, poisoned.Seat(Target)!);
        Assert.NotNull(poisonedOutcome);
        Assert.False(poisonedOutcome!.Effective);
        Assert.Equal(new[] { MalfunctionKind.Poisoned }, poisonedOutcome.Malfunctions);
    }

    /// <summary>
    /// 重获窗口终止：目标名下在这段窗口里产生的持续型效果与疯狂要求一并终止；
    /// SourceStateIndependent 的既成事实类效果（心上人醉酒）不回溯。
    /// </summary>
    [Fact]
    public void RegainEnded_CascadeTerminatesDependentsButNotFacts()
    {
        var fact = Dependent with { Id = new EffectId("test:fact"), SourceStateIndependent = true };
        var state = GameStateMachine.Fold(
        [
            Seat(Target, "dreamer", LifeState.Dead),
            Seat(Collector, "bone-collector", LifeState.Alive),
            Seat(Victim, "clockmaker", LifeState.Alive),
            Apply(Regain),
            Apply(Dependent),
            Apply(fact),
            new MadnessRequirementIssuedEvent
            {
                Requirement = new MadnessRequirement
                {
                    Id = new MadnessRequirementId("test:madness"),
                    Seat = Victim,
                    ProveToBe = "博学者",
                    Source = Target,
                    SourceCharacter = new CharacterId("cerenovus"),
                    Ability = new AbilityId("cerenovus"),
                },
            },
        ]);

        var after = ApplyTermination(state, Regain);

        var dependent = after.PersistentEffects.Single(effect => effect.Id == Dependent.Id);
        Assert.True(dependent.IsTerminated);
        Assert.Contains("R-0054", dependent.Termination!.Reason, StringComparison.Ordinal);
        Assert.False(after.PersistentEffects.Single(effect => effect.Id == fact.Id).IsTerminated);
        Assert.True(after.Seat(Victim)!.Madnesses.Single().IsTerminated);
    }

    /// <summary>来源死亡：重获窗口随既有链路终止，窗口期的效果一并失去。</summary>
    [Fact]
    public void RegainSourceDied_TerminatesWindowAndDependents()
    {
        var state = GameStateMachine.Fold(
        [
            Seat(Target, "dreamer", LifeState.Dead),
            Seat(Collector, "bone-collector", LifeState.Alive),
            Seat(Victim, "clockmaker", LifeState.Alive),
            Apply(Regain),
            Apply(Dependent),
        ]);

        var after = GameStateMachine.Apply(state, new SeatStateChangedEvent
        {
            Seat = Collector,
            Life = LifeState.Dead,
            Reason = "测试：集骨者死亡",
        });

        Assert.True(after.PersistentEffects.Single(effect => effect.Id == Regain.Id).IsTerminated);
        Assert.True(after.PersistentEffects.Single(effect => effect.Id == Dependent.Id).IsTerminated);
    }

    /// <summary>重获标记一经放置不随来源醉酒 / 中毒解除（提示标记的移除时机只有黄昏 / 死亡 / 离场）。</summary>
    [Fact]
    public void RegainWindow_SurvivesSourceDrunkAfterUse()
    {
        var regained = GameStateMachine.Apply(Baseline(), Apply(Regain));

        var drunk = GameStateMachine.Apply(regained, new SeatStateChangedEvent
        {
            Seat = Collector,
            Drunk = DrunkState.Drunk,
            Reason = "测试：集骨者事后醉酒",
        });

        Assert.True(drunk.RegainedAbilityOn(Target));
    }

    /// <summary>「这份授予还在不在」的查询：只认尚未终止的授予类效果。</summary>
    [Fact]
    public void HasLiveGrantOf_OnlyCountsLiveGrants()
    {
        var state = GameStateMachine.Apply(Baseline(), Apply(Regain));

        Assert.True(state.HasLiveGrantOf(Target, new CharacterId("dreamer")));
        Assert.False(state.HasLiveGrantOf(Target, new CharacterId("clockmaker")));

        var after = ApplyTermination(state, Regain);
        Assert.False(after.HasLiveGrantOf(Target, new CharacterId("dreamer")));
    }

    /// <summary>
    /// 入槽放行：死者没有重获窗口 → 显式跳过；重获窗口生效 → 正常发出请求
    /// （槽位本身要求存活与否由依赖声明，放行看「能力在不在」）。
    /// </summary>
    [Fact]
    public void DeadActorSlot_IsSkippedWithoutRegain_AndWokenWithIt()
    {
        var plan = StepFixture.Plan(
            "test:night-2",
            StepFixture.Action("dreamer", Target.Value, owner: "dreamer"));

        var skipped = StepMachine.StartPhase(plan, previous: null, Baseline());
        Assert.Contains(skipped.Events, gameEvent => gameEvent is PromptSkippedEvent);
        Assert.Null(skipped.State.PendingRequest);

        var woken = StepMachine.StartPhase(
            plan,
            previous: null,
            GameStateMachine.Apply(Baseline(), Apply(Regain)));
        Assert.Contains(woken.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
        Assert.Equal(Target, woken.State.PendingRequest!.Addressee);
    }

    /// <summary>
    /// 代行格（哲学家获得能力）：行动者身上那份授予不在了（二次获得替换 / 终止）→ 这一格不再唤醒他。
    /// </summary>
    [Fact]
    public void DelegatedSlot_WithoutLiveGrant_IsSkipped()
    {
        var plan = StepFixture.Plan(
            "test:night-1",
            StepSlot.GrantedAction(
                new StepSlotId("dreamer"),
                Collector,
                new CharacterId("philosopher"),
                StepFixture.Prompt("option-a"),
                [StepFixture.Alive(Collector)],
                new CharacterId("dreamer")));

        // 行动者本人是哲学家（槽位角色 = 被代行的筑梦师）。
        var baseline = GameStateMachine.Fold(
        [
            Seat(Collector, "philosopher", LifeState.Alive),
            Seat(Target, "dreamer", LifeState.Dead),
        ]);

        var without = StepMachine.StartPhase(plan, previous: null, baseline);
        Assert.Contains(without.Events, gameEvent => gameEvent is PromptSkippedEvent);

        var grant = new PersistentEffect
        {
            Id = new EffectId("test:grant"),
            Source = Collector,
            Ability = new AbilityId("philosopher.grant"),
            Target = Collector,
            SourceCharacter = new CharacterId("philosopher"),
            GrantedCharacter = new CharacterId("dreamer"),
        };
        var with = StepMachine.StartPhase(
            plan,
            previous: null,
            GameStateMachine.Apply(baseline, Apply(grant)));
        Assert.Contains(with.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    private static GameState Baseline() => GameStateMachine.Fold(
    [
        Seat(Collector, "bone-collector", LifeState.Alive),
        Seat(Target, "dreamer", LifeState.Dead),
        Seat(Victim, "clockmaker", LifeState.Alive),
    ]);

    private static PersistentEffectAppliedEvent Apply(PersistentEffect effect) =>
        new() { Effect = effect };

    private static PersistentEffectTerminatedEvent Termination(PersistentEffect effect) => new()
    {
        EffectId = effect.Id,
        Termination = new EffectTermination
        {
            Kind = EffectTerminationKind.NoLongerApplies,
            Reason = "测试：下个黄昏移除重获标记",
        },
    };

    private static GameState ApplyTermination(GameState state, PersistentEffect effect) =>
        GameStateMachine.Apply(state, Termination(effect));

    private static SeatStateChangedEvent Seat(SeatId seat, string character, LifeState life) => new()
    {
        Seat = seat,
        Character = new CharacterId(character),
        Alignment = Alignment.Good,
        Life = life,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };
}
