using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 亡骨魔「保留能力」的账本语义（R-0056）：被杀死爪牙身上有生效中的保留窗口时按「仍握有角色能力」
/// 处理（生效判定 / 效果生效 / 建表绑定 / 入槽放行的共用口径）；死亡**不再**终止他名下的持续型效果
/// 与疯狂要求（他从未失去能力——百科《死后能力保留》· 2026-10-01 抓取 · 能力简介）；
/// 保留窗口终止（亡骨魔死亡 / 离场 / 该爪牙不再是爪牙）时，这些效果与要求一并终止。
/// </summary>
public sealed class RetainedAbilityTests
{
    private static readonly SeatId Demon = new(1);
    private static readonly SeatId Minion = new(2);
    private static readonly SeatId Victim = new(4);

    private static readonly PersistentEffect Retain = new()
    {
        Id = new EffectId("test:retain:2"),
        Source = Demon,
        Ability = new AbilityId("vigormortis.retention"),
        Target = Minion,
        SourceCharacter = new CharacterId("vigormortis"),
        Window = EffectWindowKind.RetainedAbility,
    };

    private static readonly PersistentEffect Dependent = new()
    {
        Id = new EffectId("test:dependent"),
        Source = Minion,
        Ability = new AbilityId("witch"),
        Target = Victim,
        SourceCharacter = new CharacterId("witch"),
    };

    /// <summary>能力存续查询：死亡 + 生效中的保留窗口 → true；窗口终止 → false。</summary>
    [Fact]
    public void AbilityPresentOn_TracksTheRetainedWindow()
    {
        var dead = DeadMinionBaseline();
        Assert.False(dead.AbilityPresentOn(Minion));

        var retained = GameStateMachine.Apply(dead, Apply(Retain));
        Assert.True(retained.AbilityPresentOn(Minion));
        Assert.True(retained.RetainedAbilityOn(Minion));

        var removed = GameStateMachine.Apply(retained, Termination(Retain));
        Assert.False(removed.AbilityPresentOn(Minion));
        Assert.False(removed.RetainedAbilityOn(Minion));
    }

    /// <summary>保留窗口跟着**来源**的醉酒走（R-0012 的挂起口径）：醉酒 → 窗口不生效 → 能力不在。</summary>
    [Fact]
    public void RetainedWindow_FollowsTheDemonAffliction()
    {
        var retained = GameStateMachine.Apply(DeadMinionBaseline(), Apply(Retain));
        Assert.True(retained.AbilityPresentOn(Minion));

        var drunk = GameStateMachine.Apply(
            retained,
            new SeatStateChangedEvent
            {
                Seat = Demon,
                Drunk = DrunkState.Drunk,
                Reason = "测试：亡骨魔醉酒",
            });
        Assert.False(drunk.AbilityPresentOn(Minion));

        // 恢复清醒：同一效果的生效判定回来了（挂起不是移除）。
        var sober = GameStateMachine.Apply(
            drunk,
            new SeatStateChangedEvent
            {
                Seat = Demon,
                Drunk = DrunkState.Sober,
                Reason = "测试：亡骨魔恢复清醒",
            });
        Assert.True(sober.AbilityPresentOn(Minion));
    }

    /// <summary>来源维度未观测齐 → 判定不了（不猜、不映射成默认值）。</summary>
    [Fact]
    public void RetainedWindow_UnknownWhenDemonStateUnobserved()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = Demon,
                Character = new CharacterId("vigormortis"),
                Life = LifeState.Alive,
                Reason = "缺醉酒 / 中毒观测",
            },
            Seat(Minion, "witch", LifeState.Dead),
            Apply(Retain),
        ]);

        Assert.Null(state.RetainedAbilityOn(Minion));
        Assert.Null(state.AbilityPresentOn(Minion));
    }

    /// <summary>生死未观测的席位：判定不了（与集骨者窗口同款）。</summary>
    [Fact]
    public void AbilityPresentOn_IsNullWhenLifeUnobserved()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = Minion,
                Character = new CharacterId("witch"),
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "缺生死观测",
            },
        ]);

        Assert.Null(state.AbilityPresentOn(Minion));
    }

    /// <summary>
    /// 保留能力的爪牙醉酒 / 中毒时能力照样失效（百科《死后能力保留》· 能力简介：「但与其他状态的
    /// 互动仍然遵循一般规则」）：生死一维被保留窗口放行，醉酒 / 中毒两维照常否掉。
    /// </summary>
    [Fact]
    public void RetainedMinion_IsStillBlockedByPoisonAndDrunk()
    {
        var state = GameStateMachine.Apply(DeadMinionBaseline(), Apply(Retain));
        Assert.True(AbilityEffectivenessEvaluator.Evaluate(state, state.Seat(Minion)!)!.Effective);

        var poisoned = GameStateMachine.Apply(
            state,
            new SeatStateChangedEvent
            {
                Seat = Minion,
                Poison = PoisonState.Poisoned,
                Reason = "测试：爪牙中毒",
            });
        var poisonedOutcome = AbilityEffectivenessEvaluator.Evaluate(poisoned, poisoned.Seat(Minion)!);
        Assert.NotNull(poisonedOutcome);
        Assert.False(poisonedOutcome!.Effective);
        Assert.Contains(MalfunctionKind.Poisoned, poisonedOutcome.Malfunctions);

        var drunk = GameStateMachine.Apply(
            state,
            new SeatStateChangedEvent
            {
                Seat = Minion,
                Drunk = DrunkState.Drunk,
                Reason = "测试：爪牙醉酒",
            });
        var drunkOutcome = AbilityEffectivenessEvaluator.Evaluate(drunk, drunk.Seat(Minion)!);
        Assert.NotNull(drunkOutcome);
        Assert.False(drunkOutcome!.Effective);
    }

    /// <summary>
    /// 保留能力的爪牙死亡时，他名下**已落账**的持续型效果不被终止（他没有失去能力）；
    /// 保留窗口本身是来源（亡骨魔）名下的效果，目标死亡不影响它。
    /// </summary>
    [Fact]
    public void Death_DoesNotTerminateTheRetainedMinionEffects()
    {
        var state = Baseline();                                  // 2 号此刻仍存活
        state = GameStateMachine.Apply(state, Apply(Dependent)); // 女巫诅咒先落账
        state = GameStateMachine.Apply(state, Apply(Retain));    // 击杀当场落保留窗口
        state = GameStateMachine.Apply(state, Seat(Minion, "witch", LifeState.Dead));

        Assert.True(state.AbilityPresentOn(Minion));
        Assert.False(state.PersistentEffects.Single(effect => effect.Id == Dependent.Id).IsTerminated);
        Assert.False(state.PersistentEffects.Single(effect => effect.Id == Retain.Id).IsTerminated);
        Assert.True(state.IsOperative(Dependent));
    }

    /// <summary>
    /// 保留能力终止（亡骨魔死亡）→ 他名下这段窗口里的持续型效果与疯狂要求一并终止（R-0054 同族级联）。
    /// </summary>
    [Fact]
    public void LosingRetention_CascadesToTheMinionEffectsAndMadness()
    {
        var state = Baseline();
        state = GameStateMachine.Apply(state, Apply(Dependent));
        state = GameStateMachine.Apply(state, Apply(Retain));
        state = GameStateMachine.Apply(state, Seat(Minion, "witch", LifeState.Dead));
        state = GameStateMachine.Apply(state, Madness());

        state = GameStateMachine.Apply(state, Seat(Demon, "vigormortis", LifeState.Dead));

        Assert.False(state.AbilityPresentOn(Minion));
        Assert.True(state.PersistentEffects.Single(effect => effect.Id == Retain.Id).IsTerminated);
        Assert.True(state.PersistentEffects.Single(effect => effect.Id == Dependent.Id).IsTerminated);
        Assert.True(state.Seat(Victim)!.Madnesses.Single().IsTerminated);
    }

    /// <summary>已死亡的目标仍可能因不够疯狂被处决：疯狂要求的存续只看**来源**（R-0021）。</summary>
    [Fact]
    public void MadnessRequirementFromRetainedSeat_StaysOperative()
    {
        var state = Baseline();
        state = GameStateMachine.Apply(state, Apply(Retain));
        state = GameStateMachine.Apply(state, Seat(Minion, "witch", LifeState.Dead));
        state = GameStateMachine.Apply(state, Madness());

        var requirement = state.Seat(Victim)!.Madnesses.Single();
        Assert.True(state.IsOperative(requirement));
    }

    /// <summary>击杀事实折进状态账：与说书人选的侧一起留着（两条效果由常驻来源按它派生）。</summary>
    [Fact]
    public void KillRecord_IsFoldedIntoTheLedger()
    {
        var state = GameStateMachine.Apply(
            Baseline(),
            new VigormortisKillRecordedEvent
            {
                Demon = Demon,
                Minion = Minion,
                Side = SeatRingDirection.CounterClockwise,
            });

        var kill = Assert.Single(state.VigormortisKills);
        Assert.Equal(Demon, kill.Demon);
        Assert.Equal(Minion, kill.Minion);
        Assert.Equal(SeatRingDirection.CounterClockwise, kill.Side);
    }

    /// <summary>同一名爪牙不能被杀两次：重复的击杀事实是事件流损坏，显式失败（D-0014 能力 3）。</summary>
    [Fact]
    public void DuplicateKillRecord_Throws()
    {
        var recorded = new VigormortisKillRecordedEvent
        {
            Demon = Demon,
            Minion = Minion,
            Side = SeatRingDirection.Clockwise,
        };
        var state = GameStateMachine.Apply(Baseline(), recorded);

        Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(state, recorded));
    }

    /// <summary>未知的中毒侧取值：显式失败，不猜。</summary>
    [Fact]
    public void UnknownSide_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            Baseline(),
            new VigormortisKillRecordedEvent
            {
                Demon = Demon,
                Minion = Minion,
                Side = (SeatRingDirection)99,
            }));
    }

    /// <summary>离场（旅行者规则）也把保留窗口收掉并级联：标记移除时机含"亡骨魔离场"。</summary>
    [Fact]
    public void DemonDeparture_TerminatesRetentionAndCascades()
    {
        var state = Baseline();
        state = GameStateMachine.Apply(state, Apply(Dependent));
        state = GameStateMachine.Apply(state, Apply(Retain));

        state = GameStateMachine.Apply(
            state,
            new TravellerDepartedEvent { Seat = Demon, Note = "测试：亡骨魔离场" });

        Assert.True(state.PersistentEffects.Single(effect => effect.Id == Retain.Id).IsTerminated);
        Assert.True(state.PersistentEffects.Single(effect => effect.Id == Dependent.Id).IsTerminated);
    }

    private static GameState Baseline() => GameStateMachine.Fold(
    [
        Seat(Demon, "vigormortis", LifeState.Alive),
        Seat(Minion, "witch", LifeState.Alive),
        Seat(Victim, "clockmaker", LifeState.Alive),
    ]);

    /// <summary>爪牙已经被杀死的账（击杀当场已落保留窗口的常态形状）。</summary>
    private static GameState DeadMinionBaseline() => GameStateMachine.Fold(
    [
        Seat(Demon, "vigormortis", LifeState.Alive),
        Seat(Minion, "witch", LifeState.Dead),
        Seat(Victim, "clockmaker", LifeState.Alive),
    ]);

    private static MadnessRequirementIssuedEvent Madness() => new()
    {
        Requirement = new MadnessRequirement
        {
            Id = new MadnessRequirementId("test:madness"),
            Seat = Victim,
            Source = Minion,
            SourceCharacter = new CharacterId("witch"),
            Ability = new AbilityId("cerenovus"),
            ProveToBe = "clockmaker",
            ExpiresAtDay = 2,
        },
    };

    private static PersistentEffectAppliedEvent Apply(PersistentEffect effect) => new() { Effect = effect };

    private static PersistentEffectTerminatedEvent Termination(PersistentEffect effect) => new()
    {
        EffectId = effect.Id,
        Termination = new EffectTermination
        {
            Kind = EffectTerminationKind.NoLongerApplies,
            Reason = "测试：移除保留能力标记",
        },
    };

    private static SeatStateChangedEvent Seat(SeatId seat, string character, LifeState life) => new()
    {
        Seat = seat,
        Character = new CharacterId(character),
        Alignment = Alignment.Evil,
        Life = life,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };
}
