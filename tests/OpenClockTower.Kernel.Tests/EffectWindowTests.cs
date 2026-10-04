using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 咖啡师「清醒且健康」窗口的账本语义（R-0047）：窗口存续期间目标身上压制维度的效果**挂起**
/// ——标记照记、维度暂清；窗口结束且效果仍在时按**同一 EffectId** 恢复；窗口生效与否判定不了时
/// 什么都不做（不猜）；窗口自身随来源死亡 / 离场终止（R-0012 的既有链路）。
/// </summary>
public sealed class EffectWindowTests
{
    private static readonly SeatId Victim = new(1);
    private static readonly SeatId Poisoner = new(2);
    private static readonly SeatId Barista = new(9);

    private static readonly PersistentEffect Poison = new()
    {
        Id = new EffectId("test:poison"),
        Source = Poisoner,
        Ability = new AbilityId("poisoner"),
        Target = Victim,
        SourceCharacter = new CharacterId("poisoner"),
        Dimension = EffectDimension.Poison,
    };

    private static readonly PersistentEffect ImmunityWindow = new()
    {
        Id = new EffectId("test:barista-healthy"),
        Source = Barista,
        Ability = new AbilityId("barista"),
        Target = Victim,
        SourceCharacter = new CharacterId("barista"),
        Window = EffectWindowKind.AfflictionImmunity,
    };

    /// <summary>窗口生效：中毒效果被判定为「不生效」，维度由对账清空，原因点名咖啡师（R-0047 第 1 条）。</summary>
    [Fact]
    public void ImmunityWindow_SuspendsPoisonAndClearsDimension()
    {
        var state = WithWindow(Poisoned());

        Assert.False(state.IsOperative(Poison));
        Assert.True(state.WindowOn(Victim, EffectWindowKind.AfflictionImmunity));

        var change = Assert.Single(DimensionEffectReconciler.Reconcile(state).OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Healthy, change.Poison);
        Assert.Equal(Poison.Id, change.EffectId);
        Assert.Contains("咖啡师", change.Reason, StringComparison.Ordinal);
        Assert.Contains("R-0047", change.Reason, StringComparison.Ordinal);
    }

    /// <summary>窗口结束（效果终止）且中毒效果仍在：按**同一 EffectId** 恢复——不是重新施加（R-0047 第 3 条）。</summary>
    [Fact]
    public void WindowEnded_RestoresTheSameEffectLink()
    {
        var suspended = WithWindow(Poisoned());
        var cleared = GameStateMachine.Apply(
            suspended,
            Assert.Single(DimensionEffectReconciler.Reconcile(suspended).OfType<SeatStateChangedEvent>()));

        var restored = GameStateMachine.Apply(cleared, new PersistentEffectTerminatedEvent
        {
            EffectId = ImmunityWindow.Id,
            Termination = new EffectTermination
            {
                Kind = EffectTerminationKind.NoLongerApplies,
                Reason = "测试：下个黄昏移除标记",
            },
        });

        Assert.True(restored.IsOperative(Poison));
        var change = Assert.Single(DimensionEffectReconciler.Reconcile(restored).OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(Poison.Id, change.EffectId);
    }

    /// <summary>窗口内**新增**的中毒照记进效果账、但维度不翻转；窗口结束后才生效（R-0047 第 1 条末句）。</summary>
    [Fact]
    public void PoisonAppliedDuringWindow_IsRecordedButNotOperative()
    {
        var applied = GameStateMachine.Apply(WithWindow(Healthy()), new PersistentEffectAppliedEvent
        {
            Effect = Poison,
        });

        Assert.False(applied.IsOperative(Poison));
        Assert.Empty(DimensionEffectReconciler.Reconcile(applied));

        var afterWindow = GameStateMachine.Apply(applied, new PersistentEffectTerminatedEvent
        {
            EffectId = ImmunityWindow.Id,
            Termination = new EffectTermination
            {
                Kind = EffectTerminationKind.NoLongerApplies,
                Reason = "测试：下个黄昏移除标记",
            },
        });
        var change = Assert.Single(DimensionEffectReconciler.Reconcile(afterWindow).OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(Poison.Id, change.EffectId);
    }

    /// <summary>
    /// 与来源状态无关的维度效果（舞蛇人的永久中毒，R-0031）同样被窗口挂起：
    /// 免疫是**目标侧**的，不看效果自己的来源判定口径。
    /// </summary>
    [Fact]
    public void ImmunityWindow_SuspendsSourceStateIndependentPoisonToo()
    {
        var poison = Poison with { SourceStateIndependent = true };
        var state = WithWindow(GameStateMachine.Fold(
        [
            Seat(Victim, "dreamer"),
            Seat(Poisoner, "snake-charmer"),
            Seat(Barista, "barista"),
            new PersistentEffectAppliedEvent { Effect = poison },
            new SeatStateChangedEvent
            {
                Seat = Victim,
                Poison = PoisonState.Poisoned,
                Reason = "测试：永久中毒",
                EffectId = poison.Id,
            },
        ]));

        Assert.False(state.IsOperative(poison));
    }

    /// <summary>窗口的生效与否判定不了（来源维度未观测齐）：维度效果同样不猜，什么都不做。</summary>
    [Fact]
    public void UnobservedWindowSource_LeavesDimensionAlone()
    {
        // 咖啡师席位没有观测任何维度：窗口存在，但 IsOperative 判定不了。
        var state = GameStateMachine.Fold(
        [
            Seat(Victim, "dreamer"),
            Seat(Poisoner, "poisoner"),
            new PersistentEffectAppliedEvent { Effect = Poison },
            new SeatStateChangedEvent
            {
                Seat = Victim,
                Poison = PoisonState.Poisoned,
                Reason = "测试：中毒",
                EffectId = Poison.Id,
            },
            new PersistentEffectAppliedEvent { Effect = ImmunityWindow },
        ]);

        Assert.Null(state.WindowOn(Victim, EffectWindowKind.AfflictionImmunity));
        Assert.Null(state.IsOperative(Poison));
        Assert.Empty(DimensionEffectReconciler.Reconcile(state));
    }

    /// <summary>来源死亡：窗口随既有链路终止，不再挂起（R-0012 第 5 条收口）。</summary>
    [Fact]
    public void WindowSourceDied_TerminatesWindow()
    {
        var state = GameStateMachine.Apply(WithWindow(Poisoned()), new SeatStateChangedEvent
        {
            Seat = Barista,
            Life = LifeState.Dead,
            Reason = "测试：咖啡师死亡",
        });

        Assert.True(state.PersistentEffects.Single(effect => effect.Id == ImmunityWindow.Id).IsTerminated);
        Assert.Equal(false, state.WindowOn(Victim, EffectWindowKind.AfflictionImmunity));
        Assert.True(state.IsOperative(Poison));
    }

    /// <summary>窗口查询按类别区分：免疫窗口不会让「行动两次」查询变成 true。</summary>
    [Fact]
    public void WindowOn_IsCategorySpecific()
    {
        var state = WithWindow(Poisoned());

        Assert.True(state.WindowOn(Victim, EffectWindowKind.AfflictionImmunity));
        Assert.False(state.WindowOn(Victim, EffectWindowKind.SecondAction));
    }

    private static GameState Poisoned() => GameStateMachine.Fold(
    [
        Seat(Victim, "dreamer"),
        Seat(Poisoner, "poisoner"),
        Seat(Barista, "barista"),
        new PersistentEffectAppliedEvent { Effect = Poison },
        new SeatStateChangedEvent
        {
            Seat = Victim,
            Poison = PoisonState.Poisoned,
            Reason = "测试：中毒",
            EffectId = Poison.Id,
        },
    ]);

    private static GameState Healthy() => GameStateMachine.Fold(
    [
        Seat(Victim, "dreamer"),
        Seat(Poisoner, "poisoner"),
        Seat(Barista, "barista"),
    ]);

    private static GameState WithWindow(GameState state) =>
        GameStateMachine.Apply(state, new PersistentEffectAppliedEvent { Effect = ImmunityWindow });

    private static SeatStateChangedEvent Seat(SeatId seat, string character) => new()
    {
        Seat = seat,
        Character = new CharacterId(character),
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };
}
