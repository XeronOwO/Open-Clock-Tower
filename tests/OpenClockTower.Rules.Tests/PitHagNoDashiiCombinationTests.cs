using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// E15 判定行 13（残余修复）的组合用例：**创造诺-达鲺**这条角色变更 → 常驻效果对账的完整链路——
/// 新诺-达鲺立刻让邻近的两名镇民中毒；中毒者后来变成非镇民时，旧中毒按新位置关系解除、新中毒者接上。
/// </summary>
/// <remarks>
/// 来源：百科《诺-达鲺》· 2026-10-01 抓取 · 运作方式 4（「如果一名被诺-达鲺影响的镇民变成了一个非镇民角色，
/// 或者诺-达鲺变成了另一个角色，或者如果一个新的玩家变成了诺-达鲺，都会让与诺-达鲺现在邻近的镇民中毒，
/// 而之前邻近但现在不邻近的镇民恢复健康」）与范例（麻脸巫婆把活着的方古变成诺-达鲺，新诺-达鲺使邻近镇民中毒）；
/// 毒源期望与维度收口分别由 <see cref="NoDashiiPoisonSource"/> 与 <see cref="SettlementReconciler"/> 承担。
/// </remarks>
public sealed class PitHagNoDashiiCombinationTests
{
    private static readonly SeatId[] AllSeats =
        [new(1), new(2), new(3), new(4), new(5), new(6), new(7)];

    /// <summary>
    /// 开局 7 席：1 麻脸巫婆 / 2 钟表匠 / 3 呆瓜 / 4 艺术家 / 5 畸形秀演员 / 6 筑梦师 / 7 理发师。
    /// 第一步把 3 号（呆瓜）变成诺-达鲺——它的两名邻近镇民（2、4）中毒；
    /// 第二步把**中毒的** 2 号（钟表匠）变成呆瓜（非镇民）——2 号恢复健康（旧中毒解除），
    /// 逆时针方向的下一个镇民 6 号接上（新位置关系）。
    /// </summary>
    [Fact]
    public void PitHagCreatesNoDashii_PoisonsNeighbours_AndMovesOldPoisonOnCharacterChange()
    {
        var opening = GameStateMachine.Fold([
            Seat(1, "pit-hag"),
            Seat(2, "clockmaker"),
            Seat(3, "klutz"),
            Seat(4, "artist"),
            Seat(5, "mutant"),
            Seat(6, "dreamer"),
            Seat(7, "barber"),
        ]);

        // 第一步：麻脸巫婆把 3 号（呆瓜）变成诺-达鲺（不在场的恶魔）——角色变更只写角色维度。
        var first = Contract().Resolve(ResolutionContext(opening, "seat:3|no-dashii", slotIndex: 1));
        var changed = Assert.Single(first.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(3), changed.Seat);
        Assert.Equal(new CharacterId("no-dashii"), changed.Character);
        Assert.Equal(new CharacterId("klutz"), changed.PreviousCharacter);
        var afterFirst = Apply(opening, first);

        // 对账：3 号诺-达鲺顺时针最近的 4 号艺术家、逆时针最近的 2 号钟表匠中毒。
        var poison = SettlementReconciler.Reconcile(SettlementContextFor(afterFirst));
        var applied = poison.Events
            .OfType<PersistentEffectAppliedEvent>()
            .Where(item => item.Effect.Ability == NoDashiiPoisonSource.PoisonAbility)
            .ToArray();
        Assert.Equal(
            [2, 4],
            applied.Select(item => item.Effect.Target.Value).OrderBy(value => value));
        Assert.Equal(
            2,
            poison.Events.OfType<SeatStateChangedEvent>()
                .Count(item => item.Poison == PoisonState.Poisoned));

        var poisoned = Apply(afterFirst, poison.Events);
        Assert.Equal(PoisonState.Poisoned, poisoned.Seat(new SeatId(2))!.PoisonValue);
        Assert.Equal(PoisonState.Poisoned, poisoned.Seat(new SeatId(4))!.PoisonValue);
        Assert.Equal(PoisonState.Healthy, poisoned.Seat(new SeatId(6))!.PoisonValue);

        // 没有新的输入时对账幂等：不重复施加、不重复解除。
        Assert.Empty(SettlementReconciler.Reconcile(SettlementContextFor(poisoned)).Events);

        // 第二步：麻脸巫婆把中毒的 2 号（钟表匠）变成呆瓜（非镇民）——毒按新位置关系重算。
        var second = Contract().Resolve(ResolutionContext(poisoned, "seat:2|klutz", slotIndex: 1));
        var afterSecond = Apply(poisoned, second.OfType<SeatStateChangedEvent>());
        var oldPoison = poisoned.PersistentEffects.Single(
            effect => effect.Target == new SeatId(2)
                && effect.Ability == NoDashiiPoisonSource.PoisonAbility
                && !effect.IsTerminated);
        var keptPoison = poisoned.PersistentEffects.Single(
            effect => effect.Target == new SeatId(4)
                && effect.Ability == NoDashiiPoisonSource.PoisonAbility
                && !effect.IsTerminated);

        var moved = SettlementReconciler.Reconcile(SettlementContextFor(afterSecond));

        // 旧中毒解除：2 号那条中毒效果终止（条件不再满足）。
        var terminated = Assert.Single(moved.Events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(oldPoison.Id, terminated.EffectId);
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);

        // 新位置关系：逆时针下一个镇民 6 号接上；4 号保持原来的效果（不重挂）。
        var reapplied = moved.Events
            .OfType<PersistentEffectAppliedEvent>()
            .Where(item => item.Effect.Ability == NoDashiiPoisonSource.PoisonAbility)
            .ToArray();
        var nextPoison = Assert.Single(reapplied);
        Assert.Equal(new SeatId(6), nextPoison.Effect.Target);

        var movedState = Apply(afterSecond, moved.Events);
        Assert.Equal(PoisonState.Healthy, movedState.Seat(new SeatId(2))!.PoisonValue);
        Assert.Equal(PoisonState.Poisoned, movedState.Seat(new SeatId(6))!.PoisonValue);
        Assert.Equal(PoisonState.Poisoned, movedState.Seat(new SeatId(4))!.PoisonValue);

        // 4 号的中毒是原来那一条效果：位置关系没变，不重挂。
        Assert.Contains(
            movedState.PersistentEffects,
            effect => effect.Id == keptPoison.Id && !effect.IsTerminated && effect.Target == new SeatId(4));
    }

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(new CharacterId("pit-hag"))
        ?? throw new InvalidOperationException("麻脸巫婆没有注册结算契约");

    private static SettlementContext SettlementContextFor(GameState state) => new()
    {
        State = state,
        Seats = AllSeats,
        Abilities = NightActions.Resolutions,
        StandingEffects = [new NoDashiiPoisonSource()],
    };

    private static AbilityResolutionContext ResolutionContext(GameState state, string choice, int slotIndex) => new()
    {
        SlotId = new StepSlotId("pit-hag"),
        PlanLabel = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Actor = new SeatId(1),
        ActorCharacter = new CharacterId("pit-hag"),
        Seats = AllSeats,
        State = state,
        Outcome = new AbilityOutcome { Effective = true },
        Choice = choice,
        DaysStarted = 1,
        Plan = Plan(),
        SlotIndex = slotIndex,
    };

    /// <summary>其他夜晚的计划：节拍 → 麻脸巫婆 → 诺-达鲺（空槽位）→ 节拍。</summary>
    private static StepPlan Plan() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Empty(new StepSlotId("pit-hag"), new CharacterId("pit-hag")),
            StepSlot.Empty(new StepSlotId("no-dashii"), new CharacterId("no-dashii")),
            StepSlot.Beat(new StepSlotId("dawn")),
        ],
    };

    private static GameState Apply(GameState state, IEnumerable<GameEvent> events)
    {
        var next = state;
        foreach (var gameEvent in events)
        {
            next = GameStateMachine.Apply(next, gameEvent);
        }

        return next;
    }

    private static SeatStateChangedEvent Seat(int seat, string character) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId(character),
        Alignment = character is "pit-hag" or "barber" ? Alignment.Evil : Alignment.Good,
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };
}
