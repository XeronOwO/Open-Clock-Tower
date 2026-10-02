using OpenClockTower.Kernel;
using static OpenClockTower.Kernel.Tests.SeatFixture;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 效果生命周期：即时型已生效不回滚；持续型来源失效即挂起、来源恢复即继续、来源死亡即终止。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》二-3、二-7、三-3，以及《术语汇总》「死亡」；
/// "醉酒 / 中毒 = 挂起而非终止"取 <c>docs/standard/rulings.md</c> R-0012。
/// </remarks>
public sealed class EffectLifecycleTests
{
    private static readonly EffectId SharedEffect = new("test-effect");
    private static readonly SeatId SourceSeat = new(2);
    private static readonly SeatId TargetSeat = new(3);
    private static readonly AbilityId SharedAbility = new("test-ability");

    /// <summary>持续型效果只在来源存活且清醒健康时生效。</summary>
    /// <remarks>
    /// 依据：百科《重要细节》三-3——角色能力在死亡、中毒或醉酒的那一刻立即失去，
    /// 「能力产生的所有持续型效果也会终止」。
    /// </remarks>
    [Theory]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Healthy, true)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Healthy, false)]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Poisoned, false)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Poisoned, false)]
    [InlineData(LifeState.Dead, DrunkState.Sober, PoisonState.Healthy, false)]
    public void PersistentEffect_IsOperative_OnlyWhenSourceIsAliveSoberAndHealthy(
        LifeState life,
        DrunkState drunk,
        PoisonState poison,
        bool expected)
    {
        var effect = Persistent(SharedEffect);

        Assert.Equal(expected, effect.IsOperative(Seat(life: life, drunk: drunk, poison: poison)));
    }

    /// <summary>验收矩阵行 4：来源醉酒后恢复清醒 → 继续生效，且不是重新施加。</summary>
    /// <remarks>
    /// 百科《重要细节》三-3：「如果角色从中毒或醉酒中恢复健康和清醒，
    /// 角色能力和原本已经生效的持续型效果也会继续生效。」
    /// 断言同一个效果在恢复后重新生效（Id 不变、未被终止），而不是产生一条新效果。
    /// </remarks>
    [Fact]
    public void PersistentEffect_Resumes_WhenSourceSobernsUp_WithoutBeingReapplied()
    {
        var effect = Persistent(SharedEffect);
        var drunkSource = Seat(drunk: DrunkState.Drunk);
        var soberSource = drunkSource with { Drunk = DrunkState.Sober };

        Assert.False(effect.IsOperative(drunkSource));
        Assert.True(effect.IsOperative(soberSource));

        Assert.False(effect.IsTerminated);
        Assert.Equal(SharedEffect, effect.Id);
    }

    /// <summary>验收矩阵行 3：来源死亡 → 效果终止，且终止不可逆。</summary>
    /// <remarks>
    /// 百科《术语汇总》「死亡」：玩家死亡即失去角色能力，
    /// 「其角色能力所产生的任何持续性的效果也会立即终止」；
    /// 百科《重要细节》二-7：复活或角色变化者获得新角色，
    /// 「和原角色能力有关的所有持续性效果也会立即终止」——所以即使来源之后重新存活，
    /// 已终止的效果也不恢复。
    /// </remarks>
    [Fact]
    public void PersistentEffect_IsTerminatedBySourceDeath_AndStaysTerminated()
    {
        var effect = Persistent(SharedEffect);
        var deadSource = Seat(life: LifeState.Dead);

        Assert.False(effect.IsOperative(deadSource));

        var terminated = effect.Terminate(new EffectTermination
        {
            Kind = EffectTerminationKind.SourceDied,
            Reason = "来源死亡",
        });
        var sourceAliveAgain = Seat(life: LifeState.Alive);

        Assert.True(terminated.IsTerminated);
        Assert.False(terminated.IsOperative(sourceAliveAgain));
    }

    /// <summary>验收矩阵行 7：即时型效果已生效后来源中毒 → 不回滚。</summary>
    /// <remarks>
    /// 百科《重要细节》二-3 的反面：只有持续型效果会随来源失效而终止；
    /// 已经生效的即时型效果不会被撤销。同一来源上的持续型效果则挂起——两者在此对照。
    /// </remarks>
    [Fact]
    public void InstantaneousEffect_IsNotRolledBack_WhenSourceIsImpaired()
    {
        var instantaneous = new InstantaneousEffect
        {
            Id = SharedEffect,
            Source = SourceSeat,
            Ability = SharedAbility,
            Target = TargetSeat,
        };
        var persistent = Persistent(new EffectId("same-source-persistent"));

        var poisonedSource = Seat(poison: PoisonState.Poisoned);
        var deadSource = Seat(life: LifeState.Dead);

        Assert.True(instantaneous.RemainsInEffect());
        Assert.False(persistent.IsOperative(poisonedSource));

        Assert.True(instantaneous.RemainsInEffect());
        Assert.False(persistent.IsOperative(deadSource));
    }

    /// <summary>
    /// R-0031：来源状态无关的效果（舞蛇人交换后的永久中毒）只随终止结束——
    /// 来源醉酒 / 中毒都不改变它的生效判定；显式终止后才失效（死亡 / 换角色由折叠链路的
    /// 终止传播处理，不走这里）。
    /// </summary>
    /// <remarks>
    /// 百科《舞蛇人》· 2026-10-01 抓取 · 提示标记「中毒」：移除时机「放置有此标记的角色死亡或离场时」；
    /// 该效果的来源就是被标记的角色自己，因此不能让来源状态参与生效判定（否则自指震荡，
    /// `DimensionEffectReconciler` 不能收敛）。
    /// </remarks>
    [Theory]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Poisoned)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Poisoned)]
    public void PersistentEffect_SourceStateIndependent_StaysOperativeUntilTerminated(
        LifeState life,
        DrunkState drunk,
        PoisonState poison)
    {
        var effect = Persistent(SharedEffect) with { SourceStateIndependent = true };

        Assert.True(effect.IsOperative(Seat(life: life, drunk: drunk, poison: poison)));

        var terminated = effect.Terminate(new EffectTermination
        {
            Kind = EffectTerminationKind.SourceDied,
            Reason = "来源死亡",
        });

        Assert.False(terminated.IsOperative(Seat(life: life, drunk: drunk, poison: poison)));
    }

    private static PersistentEffect Persistent(EffectId id) => new()
    {
        Id = id,
        Source = SourceSeat,
        Ability = SharedAbility,
        Target = TargetSeat,
        SourceCharacter = new CharacterId("test-ability-owner"),
    };
}
