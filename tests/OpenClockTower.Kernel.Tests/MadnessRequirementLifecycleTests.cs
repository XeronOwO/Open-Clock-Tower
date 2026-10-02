using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 疯狂要求的生命周期（R-0021 / R-0012）：撤下不可逆、来源死亡或换角立即撤下、
/// 目标自己死亡**不**撤下、来源状态没观测齐时不猜。
/// </summary>
public sealed class MadnessRequirementLifecycleTests
{
    private static readonly SeatId Cerenovus = new(1);
    private static readonly SeatId Target = new(3);

    /// <summary>撤下事件落到要求上；重复撤下 / 撤下不存在的标识都显式失败（事件流顺序损坏）。</summary>
    [Fact]
    public void Termination_IsRecordedAndIrreversible()
    {
        var state = WithRequirement();

        var terminated = GameStateMachine.Apply(
            state,
            new MadnessRequirementTerminatedEvent
            {
                Id = RequirementId,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.NoLongerApplies,
                    Reason = "黎明：存续窗口结束",
                },
            });

        var requirement = Assert.Single(terminated.Seat(Target)!.Madnesses);
        Assert.True(requirement.IsTerminated);
        Assert.Equal(EffectTerminationKind.NoLongerApplies, requirement.Termination!.Kind);
        Assert.Empty(terminated.LiveRequirementsOn(Target));

        var duplicate = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            terminated,
            new MadnessRequirementTerminatedEvent
            {
                Id = RequirementId,
                Termination = new EffectTermination { Kind = EffectTerminationKind.NoLongerApplies, Reason = "再来一次" },
            }));
        Assert.Contains("已经撤下", duplicate.Message, StringComparison.Ordinal);

        var unknown = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            state,
            new MadnessRequirementTerminatedEvent
            {
                Id = new MadnessRequirementId("sv:night-9:cerenovus:madness"),
                Termination = new EffectTermination { Kind = EffectTerminationKind.NoLongerApplies, Reason = "不存在" },
            }));
        Assert.Contains("不存在", unknown.Message, StringComparison.Ordinal);
    }

    /// <summary>来源死亡 / 换角色 → 立即撤下；原因是可复核的分类 + 说明。</summary>
    [Fact]
    public void SourceDeathOrRoleChange_TerminatesTheRequirement()
    {
        var died = GameStateMachine.Apply(
            WithRequirement(),
            new SeatStateChangedEvent { Seat = Cerenovus, Life = LifeState.Dead, Reason = "被处决" });

        var afterDeath = Assert.Single(died.Seat(Target)!.Madnesses);
        Assert.True(afterDeath.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceDied, afterDeath.Termination!.Kind);

        var changed = GameStateMachine.Apply(
            WithRequirement(),
            new SeatStateChangedEvent
            {
                Seat = Cerenovus,
                Character = new CharacterId("dreamer"),
                Reason = "角色交换",
            });

        var afterChange = Assert.Single(changed.Seat(Target)!.Madnesses);
        Assert.True(afterChange.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceLostAbility, afterChange.Termination!.Kind);
    }

    /// <summary>目标自己死亡**不**撤下要求：已死亡的玩家仍可能因不够疯狂被处决（R-0021 第 2 条）。</summary>
    [Fact]
    public void TargetDeath_DoesNotTerminateTheRequirement()
    {
        var afterTargetDeath = GameStateMachine.Apply(
            WithRequirement(),
            new SeatStateChangedEvent { Seat = Target, Life = LifeState.Dead, Reason = "夜晚被击杀" });

        var requirement = Assert.Single(afterTargetDeath.Seat(Target)!.Madnesses);
        Assert.False(requirement.IsTerminated);
        Assert.Single(afterTargetDeath.LiveRequirementsOn(Target));
    }

    /// <summary>一条要求是否生效只看来源的生死 / 醉酒 / 中毒；没观测齐就返回 null（不猜，R-0012）。</summary>
    [Fact]
    public void IsOperative_FollowsTheSourceFacts_AndRefusesToGuess()
    {
        var state = WithRequirement();
        var requirement = Assert.Single(state.Seat(Target)!.Madnesses);
        Assert.Null(state.IsOperative(requirement));

        var healthy = GameStateMachine.Apply(
            state,
            new SeatStateChangedEvent
            {
                Seat = Cerenovus,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "测试：来源初始状态",
            });
        Assert.True(healthy.IsOperative(Assert.Single(healthy.Seat(Target)!.Madnesses)));

        var drunk = GameStateMachine.Apply(
            healthy,
            new SeatStateChangedEvent { Seat = Cerenovus, Drunk = DrunkState.Drunk, Reason = "涡流能力" });
        Assert.False(drunk.IsOperative(Assert.Single(drunk.Seat(Target)!.Madnesses)));
    }

    private static readonly MadnessRequirementId RequirementId = new("sv:night-1:cerenovus:madness");

    private static GameState WithRequirement() =>
        GameStateMachine.Fold(
        [
            new MadnessRequirementIssuedEvent
            {
                Requirement = new MadnessRequirement
                {
                    Id = RequirementId,
                    Seat = Target,
                    ProveToBe = "钟表匠",
                    Source = Cerenovus,
                    SourceCharacter = new CharacterId("cerenovus"),
                    Ability = new AbilityId("cerenovus.madness"),
                    ExpiresAtDay = 2,
                },
            },
        ]);
}
