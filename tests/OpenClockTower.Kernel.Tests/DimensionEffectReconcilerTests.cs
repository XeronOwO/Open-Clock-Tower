using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 按仍生效的效果重算维度（D-0015 推论 1）：生效即挂上（带 EffectId）、终止 / 挂起即解除、
/// 恢复即重挂、支持效果换人时迁移链接；判不了（来源维度没观测齐）什么都不做——不猜。
/// </summary>
public sealed class DimensionEffectReconcilerTests
{
    [Fact]
    public void OperativeEffect_AppliesDimensionWithLink()
    {
        var state = Ledger(Seat(1), Seat(2), Applied("poison-1", source: 1, target: 2));

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), change.Seat);
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("poison-1"), change.EffectId);
        Assert.Equal(new SeatId(1), change.CausedBy);
    }

    [Fact]
    public void TerminatedEffect_ReleasesDimension()
    {
        var state = Ledger(
            Seat(1),
            Seat(2),
            Applied("poison-1", 1, 2),
            PoisonFact(2, "poison-1"),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Life = LifeState.Dead,
                Reason = "测试死亡",
            });

        Assert.True(Assert.Single(state.PersistentEffects).IsTerminated);

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Healthy, change.Poison);
        Assert.Equal(new EffectId("poison-1"), change.EffectId);
    }

    [Fact]
    public void SuspendedEffect_ReleasesDimension()
    {
        var state = Ledger(
            Seat(1, drunk: DrunkState.Drunk),
            Seat(2),
            Applied("poison-1", 1, 2),
            PoisonFact(2, "poison-1"));

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Healthy, change.Poison);
        Assert.Equal(new EffectId("poison-1"), change.EffectId);
    }

    [Fact]
    public void RecoveredSource_RestoresDimensionWithLink()
    {
        var state = Ledger(
            Seat(1),
            Seat(2),
            Applied("poison-1", 1, 2),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Poison = PoisonState.Healthy,
                Reason = "挂起期间被解除",
            });

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("poison-1"), change.EffectId);
    }

    [Fact]
    public void FactWithoutEffectLink_IsNotTouched()
    {
        var state = Ledger(
            Seat(2),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Poison = PoisonState.Poisoned,
                Reason = "说书人上报",
            });

        Assert.Empty(DimensionEffectReconciler.Reconcile(state));
    }

    /// <summary>
    /// 说书人上报的中毒（无链接）遇上一条确实生效的中毒效果：引擎保留上报的值，
    /// 只把归因链接补上——值不变也产出变化事件，面板才能回答「是哪条效果」。
    /// </summary>
    [Fact]
    public void ReportedFactWithoutLink_GetsLinkFromOperativeEffect()
    {
        var state = Ledger(
            Seat(1),
            Seat(2),
            Applied("poison-1", 1, 2),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Poison = PoisonState.Poisoned,
                Reason = "说书人上报",
            });

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("poison-1"), change.EffectId);
        Assert.Contains("迁移", change.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnobservedSource_DoesNotGuess()
    {
        var state = Ledger(
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("test-poisoner"),
                Reason = "只观测到角色",
            },
            Seat(2),
            Applied("poison-1", 1, 2),
            PoisonFact(2, "poison-1"));

        Assert.Empty(DimensionEffectReconciler.Reconcile(state));
    }

    [Fact]
    public void AnotherSupportingEffect_KeepsDimensionButMovesLink()
    {
        var state = Ledger(
            Seat(1),
            Seat(2),
            Applied("poison-1", 1, 2),
            Applied("poison-2", 1, 2),
            PoisonFact(2, "poison-1"),
            new PersistentEffectTerminatedEvent
            {
                EffectId = new EffectId("poison-1"),
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.StorytellerVoided,
                    Reason = "测试作废",
                },
            });

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("poison-2"), change.EffectId);
    }

    /// <summary>
    /// R-0031：来源 = 目标本人的「来源状态无关」中毒（舞蛇人交换后的永久中毒）——
    /// 本人醉酒 / 中毒时效果照常生效，不会出现「挂起 → 解除 → 恢复」的自指震荡。
    /// </summary>
    [Theory]
    [InlineData(DrunkState.Sober, PoisonState.Poisoned)]
    [InlineData(DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(DrunkState.Drunk, PoisonState.Poisoned)]
    public void SelfSourcedSourceStateIndependentEffect_StaysOperative(DrunkState drunk, PoisonState poison)
    {
        var state = Ledger(
            Seat(2, drunk: drunk) with { Poison = poison },
            Applied("snake-charmer.poison", source: 2, target: 2, sourceStateIndependent: true));

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("snake-charmer.poison"), change.EffectId);
    }

    /// <summary>
    /// R-0031 第 3 条：来源状态无关的效果仍随「来源死亡 / 换角色」终止——本人死亡后中毒解除。
    /// </summary>
    [Fact]
    public void SelfSourcedEffect_TerminatesWithHolderDeath_AndReleasesDimension()
    {
        var state = Ledger(
            Seat(2),
            Applied("snake-charmer.poison", 2, 2, sourceStateIndependent: true),
            PoisonFact(2, "snake-charmer.poison"),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Life = LifeState.Dead,
                Reason = "测试死亡",
            });

        Assert.True(Assert.Single(state.PersistentEffects).IsTerminated);

        var events = DimensionEffectReconciler.Reconcile(state);

        var change = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Healthy, change.Poison);
        Assert.Equal(new EffectId("snake-charmer.poison"), change.EffectId);
    }

    private static GameState Ledger(params GameEvent[] events) => GameStateMachine.Fold(events);

    private static SeatStateChangedEvent Seat(int seat, DrunkState drunk = DrunkState.Sober) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId("test-poisoner"),
        Life = LifeState.Alive,
        Drunk = drunk,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };

    private static PersistentEffectAppliedEvent Applied(
        string id,
        int source,
        int target,
        bool sourceStateIndependent = false) => new()
        {
            Effect = new PersistentEffect
            {
                Id = new EffectId(id),
                Source = new SeatId(source),
                Ability = new AbilityId("test.poison"),
                Target = new SeatId(target),
                SourceCharacter = new CharacterId("test-poisoner"),
                Dimension = EffectDimension.Poison,
                SourceStateIndependent = sourceStateIndependent,
            },
        };

    private static SeatStateChangedEvent PoisonFact(int seat, string effectId) => new()
    {
        Seat = new SeatId(seat),
        Poison = PoisonState.Poisoned,
        Reason = "测试：效果生效",
        EffectId = new EffectId(effectId),
    };
}
