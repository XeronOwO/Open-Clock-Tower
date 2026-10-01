using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 固定点对账：常驻效果期望集 → 补缺 / 终止 → 维度重算；幂等（没变化就不产事件）；
/// 判定不了时不重算但留说明；规则互相冲突（不收敛）时显式失败。
/// </summary>
public sealed class SettlementReconcilerTests
{
    [Fact]
    public void StandingExpectation_IsAppliedWithDimension_AndIsIdempotent()
    {
        var source = new FakeSource(StandingEffectAssessment.Conclusive([Expectation("fake:1", target: 2)]));
        var context = Context(source, Seat(1), Seat(2));

        var result = SettlementReconciler.Reconcile(context);

        Assert.Contains(result.Events, gameEvent => gameEvent is PersistentEffectAppliedEvent);
        var change = Assert.Single(result.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Poisoned, change.Poison);
        Assert.Equal(new EffectId("fake:1"), change.EffectId);
        Assert.Equal(new SeatId(1), change.CausedBy);

        var settled = Fold(context.State, result.Events);
        var again = SettlementReconciler.Reconcile(context.WithState(settled));
        Assert.Empty(again.Events);
        Assert.Empty(again.Diagnostics);
    }

    [Fact]
    public void RemovedExpectation_TerminatesEffectAndReleasesDimension()
    {
        var source = new FakeSource(StandingEffectAssessment.Conclusive([Expectation("fake:1", target: 2)]));
        var context = Context(source, Seat(1), Seat(2));
        var settled = Fold(context.State, SettlementReconciler.Reconcile(context).Events);

        source.Assessment = StandingEffectAssessment.Conclusive([]);
        var result = SettlementReconciler.Reconcile(context.WithState(settled));

        var terminated = Assert.Single(result.Events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);
        var change = Assert.Single(result.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(PoisonState.Healthy, change.Poison);
        Assert.Equal(new EffectId("fake:1"), change.EffectId);
    }

    /// <summary>
    /// 来源死亡 → 期望消失 → 效果终止；来源复活 → 期望重回 → **新的一条效果**（新标识）＋维度重挂。
    /// 终止不可逆，但「重新获得同一能力」是另一回事（R-0012）。
    /// </summary>
    [Fact]
    public void ExpectationReturningAfterTermination_IsReappliedAsNewGeneration()
    {
        var source = new FakeSource(StandingEffectAssessment.Conclusive([Expectation("fake:1", target: 2)]));
        var context = Context(source, Seat(1), Seat(2));

        var applied = Fold(context.State, SettlementReconciler.Reconcile(context).Events);
        Assert.Equal(PoisonState.Poisoned, applied.Seat(new SeatId(2))!.PoisonValue);

        // 来源死亡：真实来源（如诺-达鲺）此时不再给出期望——效果被折叠终止、维度被解除。
        source.Assessment = StandingEffectAssessment.Conclusive([]);
        var dead = GameStateMachine.Apply(applied, new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Life = LifeState.Dead,
            Reason = "测试死亡",
        });
        var released = Fold(dead, SettlementReconciler.Reconcile(context.WithState(dead)).Events);
        Assert.True(Assert.Single(released.PersistentEffects).IsTerminated);
        Assert.Equal(PoisonState.Healthy, released.Seat(new SeatId(2))!.PoisonValue);

        // 来源复活：期望重回 → 不是让旧效果复活，而是产生新的一条（新标识）并重新挂上维度。
        source.Assessment = StandingEffectAssessment.Conclusive([Expectation("fake:1", target: 2)]);
        var revived = GameStateMachine.Apply(released, new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Life = LifeState.Alive,
            Reason = "测试复活",
        });
        var result = SettlementReconciler.Reconcile(context.WithState(revived));

        var reapplied = Assert.Single(result.Events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(new EffectId("fake:1#2"), reapplied.Effect.Id);

        var settled = Fold(revived, result.Events);
        var fact = settled.Seat(new SeatId(2))!.Poison;
        Assert.Equal(PoisonState.Poisoned, fact!.Value);
        Assert.Equal(new EffectId("fake:1#2"), fact.EffectId);

        Assert.Empty(SettlementReconciler.Reconcile(context.WithState(settled)).Events);
    }

    [Fact]
    public void InconclusiveSource_ReportsDiagnosticWithoutChangingAnything()
    {
        var source = new FakeSource(StandingEffectAssessment.Inconclusive("席位 1 的角色尚未观测"));
        var result = SettlementReconciler.Reconcile(Context(source, Seat(1)));

        Assert.Empty(result.Events);
        Assert.Contains("角色尚未观测", Assert.Single(result.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void NonConvergentRules_FailExplicitly()
    {
        var context = Context(new NonConvergentSource(), Seat(1), Seat(2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => SettlementReconciler.Reconcile(context));
        Assert.Contains("没有收敛", exception.Message, StringComparison.Ordinal);
    }

    private static SettlementContext Context(IStandingEffectSource source, params GameEvent[] seatEvents) => new()
    {
        State = GameStateMachine.Fold(seatEvents),
        Seats = [new SeatId(1), new SeatId(2)],
        Abilities = new EmptyCatalog(),
        StandingEffects = [source],
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

    private static StandingEffectExpectation Expectation(string id, int target) => new()
    {
        Id = new EffectId(id),
        Ability = new AbilityId("fake"),
        Source = new SeatId(1),
        SourceCharacter = new CharacterId("test-source"),
        Target = new SeatId(target),
        Dimension = EffectDimension.Poison,
    };

    private static SeatStateChangedEvent Seat(int seat) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId("test-source"),
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };

    private sealed class EmptyCatalog : IAbilityResolutionCatalog
    {
        public IAbilityResolution? Find(CharacterId character) => null;
    }

    private sealed class FakeSource : IStandingEffectSource
    {
        internal FakeSource(StandingEffectAssessment assessment)
        {
            Assessment = assessment;
        }

        public AbilityId Ability { get; } = new("fake");

        internal StandingEffectAssessment Assessment { get; set; }

        public StandingEffectAssessment Evaluate(StandingEffectContext context) => Assessment;
    }

    /// <summary>每轮都期望一条新效果：规则互相打架，对账必须显式失败而不是无限打转。</summary>
    private sealed class NonConvergentSource : IStandingEffectSource
    {
        private int _counter;

        public AbilityId Ability { get; } = new("fake");

        public StandingEffectAssessment Evaluate(StandingEffectContext context) =>
            StandingEffectAssessment.Conclusive(
            [
                new StandingEffectExpectation
                {
                    Id = new EffectId($"fake:{++_counter}"),
                    Ability = Ability,
                    Source = new SeatId(1),
                    SourceCharacter = new CharacterId("test-source"),
                    Target = new SeatId(2),
                    Dimension = EffectDimension.Poison,
                },
            ]);
    }
}
