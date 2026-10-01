using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 效果归因链：谁施加、用哪个能力、作用于谁、何时因何终止。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》二-3 / 二-7 / 三-3（持续型效果随来源死亡或角色变化立即终止、
/// 随来源醉酒中毒挂起、恢复后继续；即时型效果不回滚）。
/// </remarks>
public sealed class EffectAttributionTests
{
    private static readonly SeatId Poisoner = new(5);
    private static readonly SeatId Victim = new(3);
    private static readonly SeatId Demon = new(2);
    private static readonly EffectId PoisonEffectId = new("poisoner-poison:5:3");
    private static readonly EffectId KillEffectId = new("imp-kill:2:3");
    private static readonly AbilityId PoisonAbility = new("poisoner");
    private static readonly AbilityId KillAbility = new("imp");
    private static readonly CharacterId PoisonerCharacter = new("poisoner");
    private static readonly CharacterId SeamstressCharacter = new("seamstress");

    /// <summary>施加一条持续型效果：谁施加、用哪个能力、作用于谁，一个都不能少。</summary>
    [Fact]
    public void PersistentEffect_IsRecordedWithWhoAndWhatAndWhom()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
        ]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.Equal(Poisoner, effect.Source);
        Assert.Equal(PoisonAbility, effect.Ability);
        Assert.Equal(Victim, effect.Target);
        Assert.False(effect.IsTerminated);

        Assert.Single(state.EffectsSourcedBy(Poisoner));
        Assert.True(state.IsOperative(effect));
        Assert.Single(state.OperativeEffectsOn(Victim));
    }

    /// <summary>来源死亡 → 其持续型效果立即终止并写明原因；来源复活也不恢复（终止不可逆）。</summary>
    [Fact]
    public void SourceDeath_TerminatesItsEffects_AndTerminationIsIrreversible()
    {
        var died = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent
            {
                Seat = Poisoner,
                Life = LifeState.Dead,
                Reason = "白天被处决",
            },
        ]);

        var effect = Assert.Single(died.EffectsOn(Victim));
        Assert.True(effect.IsTerminated);
        Assert.NotNull(effect.Termination);
        Assert.Equal(EffectTerminationKind.SourceDied, effect.Termination.Kind);
        Assert.Contains("死亡", effect.Termination.Reason);
        Assert.False(died.IsOperative(effect));

        var revived = GameStateMachine.Apply(
            died,
            new SeatStateChangedEvent { Seat = Poisoner, Life = LifeState.Alive, Reason = "复活" });

        var afterRevival = Assert.Single(revived.EffectsOn(Victim));
        Assert.True(afterRevival.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceDied, afterRevival.Termination!.Kind);
    }

    /// <summary>
    /// 来源换角色 → 原角色能力消失 → 其持续型效果立即终止（百科《重要细节》二-7）。
    /// </summary>
    [Fact]
    public void SourceCharacterChange_TerminatesItsEffects()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent
            {
                Seat = Poisoner,
                Character = SeamstressCharacter,
                Reason = "理发师：交换角色",
                CausedBy = Demon,
            },
        ]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.True(effect.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceLostAbility, effect.Termination!.Kind);
        Assert.Equal(Demon, effect.Termination.CausedBy);
    }

    /// <summary>
    /// 复核补丁（原盲区）：来源的角色**从未被观测过**，第一次观测到就已经是新角色时也必须终止。
    /// 判定依据是效果自己记录的施加时角色，而不是"上一次观测到的角色"——后者会静默漏判。
    /// </summary>
    [Fact]
    public void SourceCharacterFirstObservedAsDifferent_TerminatesItsEffects()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent
            {
                Seat = Poisoner,
                Character = SeamstressCharacter,
                Reason = "第一次观测到 5 号的角色：早就换过了",
            },
        ]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.True(effect.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceLostAbility, effect.Termination!.Kind);
        Assert.NotEqual(true, state.IsOperative(effect));
    }

    /// <summary>观测到的角色与施加时一致 → 能力还在，效果不动：不能把"报了一次角色"当成换角色。</summary>
    [Fact]
    public void SourceCharacterReobservedAsSame_KeepsTheEffectOperative()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent { Seat = Poisoner, Character = PoisonerCharacter, Reason = "重申角色" },
        ]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.False(effect.IsTerminated);
        Assert.True(state.IsOperative(effect));
    }

    /// <summary>同一条事件里既报死亡又报换角色：按死亡终止（更强、且不可逆的那个原因）。</summary>
    [Fact]
    public void DeathAndCharacterChangeInOneEvent_TerminatesWithSourceDied()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent
            {
                Seat = Poisoner,
                Life = LifeState.Dead,
                Character = SeamstressCharacter,
                Reason = "同一条事件里既死亡又换角色",
            },
        ]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.Equal(EffectTerminationKind.SourceDied, effect.Termination!.Kind);
    }

    /// <summary>LiveEffectsOn 只给未终止的；已终止的仍留在 EffectsOn 里——那正是"为什么解毒了"的答案。</summary>
    [Fact]
    public void LiveEffectsOn_ExcludesTerminatedEffects()
    {
        var state = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new SeatStateChangedEvent { Seat = Poisoner, Life = LifeState.Dead, Reason = "被投死" },
        ]);

        Assert.Empty(state.LiveEffectsOn(Victim));
        Assert.Empty(state.OperativeEffectsOn(Victim));
        Assert.Single(state.EffectsOn(Victim));
    }

    /// <summary>同一个裁定点重复签发同一条疯狂要求 = 事件流损坏，与效果路径同一失败姿态。</summary>
    [Fact]
    public void DuplicateMadnessRequirement_IsRejectedLoudly()
    {
        var requirement = new MadnessRequirement
        {
            Seat = Victim,
            ProveToBe = "clockmaker",
            IssuedBy = new DecisionPointId("sv:night-1:cerenovus:decision"),
        };
        var state = GameStateMachine.Fold([new MadnessRequirementIssuedEvent { Requirement = requirement }]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GameStateMachine.Apply(state, new MadnessRequirementIssuedEvent { Requirement = requirement }));

        Assert.Contains("不能重复签发", exception.Message);
    }

    /// <summary>来源醉酒 / 中毒**不终止**效果，只是让它暂时不生效；来源恢复后继续生效，且不是重新施加。</summary>
    [Fact]
    public void SourceImpaired_SuspendsWithoutTerminating_AndResumesWhenSober()
    {
        var healthy = GameStateMachine.Fold(
        [
            HealthySeat(Poisoner, "开局：说书人分配"),
            new PersistentEffectAppliedEvent { Effect = Poison() },
        ]);

        var impaired = GameStateMachine.Apply(
            healthy,
            new SeatStateChangedEvent { Seat = Poisoner, Drunk = DrunkState.Drunk, Reason = "涡流能力" });

        var suspended = Assert.Single(impaired.EffectsOn(Victim));
        Assert.False(suspended.IsTerminated);
        Assert.False(impaired.IsOperative(suspended));
        Assert.Empty(impaired.OperativeEffectsOn(Victim));

        var recovered = GameStateMachine.Apply(
            impaired,
            new SeatStateChangedEvent { Seat = Poisoner, Drunk = DrunkState.Sober, Reason = "醉意消退" });

        var resumed = Assert.Single(recovered.OperativeEffectsOn(Victim));
        Assert.Single(recovered.PersistentEffects);
        Assert.Equal(PoisonEffectId, resumed.Id);
    }

    /// <summary>来源的生死 / 醉酒 / 中毒还没观测齐时，判定结果是"不知道"，不是"生效"也不是"失效"。</summary>
    [Fact]
    public void IsOperative_IsUnknownWhenSourceDimensionsWereNeverObserved()
    {
        var state = GameStateMachine.Fold([new PersistentEffectAppliedEvent { Effect = Poison() }]);

        var effect = Assert.Single(state.EffectsOn(Victim));
        Assert.Null(state.IsOperative(effect));
        Assert.Empty(state.OperativeEffectsOn(Victim));
    }

    /// <summary>即时型效果记进账里，且来源之后失效也不回滚。</summary>
    [Fact]
    public void InstantaneousEffect_IsRecordedAndNeverRolledBack()
    {
        var state = GameStateMachine.Fold(
        [
            new InstantaneousEffectAppliedEvent
            {
                Effect = new InstantaneousEffect
                {
                    Id = KillEffectId,
                    Source = Demon,
                    Ability = KillAbility,
                    Target = Victim,
                },
            },
            new SeatStateChangedEvent { Seat = Demon, Life = LifeState.Dead, Reason = "被投死" },
        ]);

        var effect = Assert.Single(state.InstantaneousEffectsOn(Victim));
        Assert.Equal(Demon, effect.Source);
        Assert.Equal(KillAbility, effect.Ability);
        Assert.True(effect.RemainsInEffect());
        Assert.Empty(state.PersistentEffects);
    }

    /// <summary>折叠推导不出来的终止（说书人强制作废）走显式终止事件，原因照记。</summary>
    [Fact]
    public void ExplicitTermination_RecordsStorytellerVoid()
    {
        var applied = GameStateMachine.Fold([new PersistentEffectAppliedEvent { Effect = Poison() }]);

        var voided = GameStateMachine.Apply(
            applied,
            new PersistentEffectTerminatedEvent
            {
                EffectId = PoisonEffectId,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.StorytellerVoided,
                    Reason = "说书人判定该效果不再适用",
                },
            });

        var effect = Assert.Single(voided.EffectsOn(Victim));
        Assert.True(effect.IsTerminated);
        Assert.Equal(EffectTerminationKind.StorytellerVoided, effect.Termination!.Kind);
        Assert.Null(effect.Termination.CausedBy);
    }

    /// <summary>终止一条不存在的效果 = 事件流损坏，必须显式失败。</summary>
    [Fact]
    public void TerminatingUnknownEffect_IsRejectedLoudly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            GameState.Empty,
            new PersistentEffectTerminatedEvent
            {
                EffectId = PoisonEffectId,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.StorytellerVoided,
                    Reason = "无中生有的终止",
                },
            }));

        Assert.Contains("不存在", exception.Message);
    }

    /// <summary>重复终止同一条效果 = 事件流损坏，必须显式失败。</summary>
    [Fact]
    public void TerminatingAlreadyTerminatedEffect_IsRejectedLoudly()
    {
        var already = GameStateMachine.Fold(
        [
            new PersistentEffectAppliedEvent { Effect = Poison() },
            new PersistentEffectTerminatedEvent
            {
                EffectId = PoisonEffectId,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.StorytellerVoided,
                    Reason = "第一次终止",
                },
            },
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            already,
            new PersistentEffectTerminatedEvent
            {
                EffectId = PoisonEffectId,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.StorytellerVoided,
                    Reason = "第二次终止",
                },
            }));

        Assert.Contains("已经终止", exception.Message);
    }

    /// <summary>同一个效果标识不能同时存在两条活效果：账认不出谁是谁。</summary>
    [Fact]
    public void DuplicatePersistentEffectId_IsRejectedLoudly()
    {
        var state = GameStateMachine.Fold([new PersistentEffectAppliedEvent { Effect = Poison() }]);

        var exception = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            state,
            new PersistentEffectAppliedEvent { Effect = Poison() }));

        Assert.Contains("不能重复施加", exception.Message);
    }

    /// <summary>疯狂只由裁定写入（R-0003）：它挂在账上，但不由引擎判定，也不动任何其它维度。</summary>
    [Fact]
    public void MadnessRequirement_IsRecordedOnTheSeatOnly()
    {
        var state = GameStateMachine.Fold(
        [
            new MadnessRequirementIssuedEvent
            {
                Requirement = new MadnessRequirement
                {
                    Seat = Victim,
                    ProveToBe = "clockmaker",
                    IssuedBy = new DecisionPointId("sv:night-1:cerenovus:decision"),
                },
            },
        ]);

        var entry = Assert.IsType<SeatStateEntry>(state.Seat(Victim));
        var requirement = Assert.Single(entry.Madnesses);
        Assert.Equal("clockmaker", requirement.ProveToBe);
        Assert.Null(entry.Life);
        Assert.Null(entry.Character);
        Assert.Null(entry.Alignment);
        Assert.Null(entry.Drunk);
        Assert.Null(entry.Poison);
    }

    private static SeatStateChangedEvent HealthySeat(SeatId seat, string reason) => new()
    {
        Seat = seat,
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = reason,
    };

    private static PersistentEffect Poison() => new()
    {
        Id = PoisonEffectId,
        Source = Poisoner,
        Ability = PoisonAbility,
        Target = Victim,
        SourceCharacter = PoisonerCharacter,
    };
}
