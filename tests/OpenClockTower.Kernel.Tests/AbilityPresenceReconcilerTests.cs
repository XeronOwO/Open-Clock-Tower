using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 能力存续对账（女巫「只剩三名存活玩家时失去能力」的通用落点）：
/// 能力已失去 → 它名下的持续型效果立即解除（不可逆）；判定不了 → 什么都不做并留说明；
/// 只动自己名下的效果。
/// </summary>
public sealed class AbilityPresenceReconcilerTests
{
    [Fact]
    public void AbsentAbility_TerminatesItsLiveEffects()
    {
        var context = Context([new FakePresence("fake", present: false)], Seat(1), Seat(2), Cursed("fake", target: 2));

        var result = SettlementReconciler.Reconcile(context);

        var terminated = Assert.Single(result.Events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(new EffectId("fake:curse"), terminated.EffectId);
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);
        Assert.Contains("fake", terminated.Termination.Reason, StringComparison.Ordinal);

        var settled = Fold(context.State, result.Events);
        Assert.True(Assert.Single(settled.PersistentEffects).IsTerminated);

        // 幂等：解除之后再对账不产出任何事件。
        Assert.Empty(SettlementReconciler.Reconcile(context.WithState(settled)).Events);
    }

    [Fact]
    public void PresentAbility_KeepsItsEffects()
    {
        var context = Context([new FakePresence("fake", present: true)], Seat(1), Seat(2), Cursed("fake", target: 2));

        var result = SettlementReconciler.Reconcile(context);

        Assert.Empty(result.Events);
        Assert.Empty(result.Diagnostics);
        Assert.False(Assert.Single(context.State.PersistentEffects).IsTerminated);
    }

    /// <summary>判定不了（输入不全）→ 不解除并留说明：解除是破坏性动作，必须有据（不猜，D-0015）。</summary>
    [Fact]
    public void IndeterminatePresence_TerminatesNothingAndReportsDiagnostic()
    {
        var context = Context([new FakePresence("fake", present: null)], Seat(1), Seat(2), Cursed("fake", target: 2));

        var result = SettlementReconciler.Reconcile(context);

        Assert.Empty(result.Events);
        Assert.Contains("未判定", Assert.Single(result.Diagnostics), StringComparison.Ordinal);
        Assert.False(Assert.Single(context.State.PersistentEffects).IsTerminated);
    }

    [Fact]
    public void AbsentAbility_DoesNotTouchOtherAbilities()
    {
        var context = Context(
            [new FakePresence("fake", present: false)],
            Seat(1),
            Seat(2),
            Cursed("fake", target: 2),
            Cursed("other", target: 2));

        var result = SettlementReconciler.Reconcile(context);

        var terminated = Assert.Single(result.Events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(new EffectId("fake:curse"), terminated.EffectId);

        var settled = Fold(context.State, result.Events);
        Assert.True(settled.PersistentEffects.Single(effect => effect.Ability == new AbilityId("fake")).IsTerminated);
        Assert.False(settled.PersistentEffects.Single(effect => effect.Ability == new AbilityId("other")).IsTerminated);
    }

    private static SettlementContext Context(
        IReadOnlyList<IAbilityPresence> presences,
        params GameEvent[] seatEvents) => new()
        {
            State = GameStateMachine.Fold(seatEvents),
            Seats = [new SeatId(1), new SeatId(2)],
            Abilities = new EmptyCatalog(),
            AbilityPresences = presences,
        };

    private static GameState Fold(GameState state, IReadOnlyList<GameEvent> events)
    {
        var next = state;
        foreach (var gameEvent in events)
        {
            next = GameStateMachine.Apply(next, gameEvent);
        }

        return next;
    }

    private static PersistentEffectAppliedEvent Cursed(string ability, int target) => new()
    {
        Effect = new PersistentEffect
        {
            Id = new EffectId($"{ability}:curse"),
            Source = new SeatId(1),
            Ability = new AbilityId(ability),
            Target = new SeatId(target),

            // 必须与来源席位当下的角色一致：角色一变，折叠层就把效果当作"来源失去能力"终止。
            SourceCharacter = new CharacterId("test-1"),
        },
    };

    private static SeatStateChangedEvent Seat(int seat) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId($"test-{seat}"),
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };

    private sealed class EmptyCatalog : IAbilityResolutionCatalog
    {
        public IAbilityResolution? Find(CharacterId character) => null;
    }

    private sealed class FakePresence : IAbilityPresence
    {
        private readonly bool? _present;

        internal FakePresence(string ability, bool? present)
        {
            Ability = new AbilityId(ability);
            _present = present;
        }

        public AbilityId Ability { get; }

        public bool? IsPresent(AbilityPresenceContext context) => _present;
    }
}
