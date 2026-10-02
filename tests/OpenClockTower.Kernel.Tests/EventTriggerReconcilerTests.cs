using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 事件触发的有界级联：产出的后果折进账并回流；没有后果就停；触发器互相引发（不收敛）显式失败；
/// 触发器返回 null 视为缺陷。
/// </summary>
/// <remarks>
/// 女巫的「提名即死」是首位消费者，但这一层只验证**管线**行为（规则语义在 Rules.Tests）：
/// 内核不认识角色，只认识「事件 → 后果」这条契约。
/// </remarks>
public sealed class EventTriggerReconcilerTests
{
    [Fact]
    public void Consequence_IsFoldedIntoStateAndReturned()
    {
        var trigger = new RecordingTrigger("fake", context =>
        {
            if (context.Events.OfType<NominationMadeEvent>().FirstOrDefault() is not { } nomination
                || context.State.Seat(nomination.Nominator)?.LifeValue != LifeState.Alive)
            {
                return [];
            }

            return
            [
                new SeatStateChangedEvent
                {
                    Seat = nomination.Nominator,
                    Life = LifeState.Dead,
                    Reason = "测试：触发致死",
                },
            ];
        });

        var context = Context([trigger], Seat(1), Seat(2));
        var nomination = new NominationMadeEvent
        {
            DayNumber = 1,
            NominationIndex = 1,
            Nominator = new SeatId(2),
            Nominee = new SeatId(1),
        };

        var result = EventTriggerReconciler.Reconcile(context.State, context, [nomination]);

        var death = Assert.Single(result.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, result.State.Seat(new SeatId(2))!.LifeValue);
        Assert.Contains("fake", Assert.Single(result.Diagnostics), StringComparison.Ordinal);

        // 第二轮（后果事件）触发器仍然被求值，但条件在账上已不成立：不再产出第二条死亡事实。
        Assert.Equal(2, trigger.Calls.Count);
        Assert.Same(nomination, Assert.Single(trigger.Calls[0].Events));
        Assert.Single(trigger.Calls[1].Events);
    }

    [Fact]
    public void CascadedConsequences_AreEvaluatedUntilStable()
    {
        var first = new RecordingTrigger("first", context =>
            context.State.PersistentEffects.Any(effect => effect.Id == new EffectId("test:cascade"))
                ? []
                :
                [
                    new PersistentEffectAppliedEvent
                    {
                        Effect = new PersistentEffect
                        {
                            Id = new EffectId("test:cascade"),
                            Source = new SeatId(1),
                            Ability = new AbilityId("first"),
                            Target = new SeatId(2),
                            SourceCharacter = new CharacterId("test-source"),
                        },
                    },
                ]);

        var second = new RecordingTrigger("second", context =>
            context.Events.OfType<PersistentEffectAppliedEvent>().Any()
                ? [
                    new SeatStateChangedEvent
                    {
                        Seat = new SeatId(2),
                        Life = LifeState.Dead,
                        Reason = "测试：二级后果",
                    },
                ]
                : []);

        var context = Context([first, second], Seat(1), Seat(2));
        var result = EventTriggerReconciler.Reconcile(context.State, context, [Seat(1)]);

        Assert.Equal(
            [typeof(PersistentEffectAppliedEvent), typeof(SeatStateChangedEvent)],
            result.Events.Select(gameEvent => gameEvent.GetType()));
        Assert.Single(result.State.PersistentEffects);
        Assert.Equal(LifeState.Dead, result.State.Seat(new SeatId(2))!.LifeValue);
    }

    [Fact]
    public void WithoutTriggers_NothingHappens()
    {
        var context = Context([], Seat(1), Seat(2));
        var result = EventTriggerReconciler.Reconcile(context.State, context, [Seat(1)]);

        Assert.Empty(result.Events);
        Assert.Empty(result.Diagnostics);
        Assert.Same(context.State, result.State);
    }

    [Fact]
    public void NoConsequence_ReturnsInputState()
    {
        var trigger = new RecordingTrigger("fake", _ => []);
        var context = Context([trigger], Seat(1), Seat(2));

        var result = EventTriggerReconciler.Reconcile(context.State, context, [Seat(1)]);

        Assert.Empty(result.Events);
        Assert.Empty(result.Diagnostics);
        Assert.Same(context.State, result.State);
    }

    [Fact]
    public void NonConvergentTriggers_FailExplicitly()
    {
        var counter = 0;
        var trigger = new RecordingTrigger("fake", _ =>
            [
                new PersistentEffectAppliedEvent
                {
                    Effect = new PersistentEffect
                    {
                        Id = new EffectId($"test:runaway:{++counter}"),
                        Source = new SeatId(1),
                        Ability = new AbilityId("fake"),
                        Target = new SeatId(2),
                        SourceCharacter = new CharacterId("test-source"),
                    },
                },
            ]);
        var context = Context([trigger], Seat(1), Seat(2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => EventTriggerReconciler.Reconcile(context.State, context, [Seat(1)]));
        Assert.Contains("没有收敛", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullReturningTrigger_FailsExplicitly()
    {
        var trigger = new RecordingTrigger("fake", _ => null!);
        var context = Context([trigger], Seat(1), Seat(2));

        var exception = Assert.Throws<InvalidOperationException>(
            () => EventTriggerReconciler.Reconcile(context.State, context, [Seat(1)]));
        Assert.Contains("返回了 null", exception.Message, StringComparison.Ordinal);
    }

    private static SettlementContext Context(
        IReadOnlyList<IEventTrigger> triggers,
        params GameEvent[] seatEvents) => new()
        {
            State = GameStateMachine.Fold(seatEvents),
            Seats = [new SeatId(1), new SeatId(2)],
            Abilities = new EmptyCatalog(),
            EventTriggers = triggers,
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

    private sealed class RecordingTrigger : IEventTrigger
    {
        private readonly Func<EventTriggerContext, IReadOnlyList<GameEvent>> _evaluate;

        internal RecordingTrigger(string ability, Func<EventTriggerContext, IReadOnlyList<GameEvent>> evaluate)
        {
            Ability = new AbilityId(ability);
            _evaluate = evaluate;
        }

        public AbilityId Ability { get; }

        internal List<EventTriggerContext> Calls { get; } = [];

        public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
        {
            Calls.Add(context);
            return _evaluate(context);
        }
    }
}
