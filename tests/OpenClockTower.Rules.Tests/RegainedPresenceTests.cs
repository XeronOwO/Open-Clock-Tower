using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 能力存续族的重获感知（R-0054）：死亡但有生效中重获窗口的席位按「仍握有角色能力」处理——
/// 女巫诅咒继续有效并在窗口结束时解除、诺-达鲺继续下毒、涡流继续让镇民信息必假
/// （百科《集骨者》范例：女巫醒来并诅咒了钟表匠；《获得能力》：直到这项能力失效之前持续生效）。
/// </summary>
/// <remarks>通过公开目录 <see cref="RoleContracts"/> / <see cref="NightActions"/> 取实现——与运行时同一批对象。</remarks>
public sealed class RegainedPresenceTests
{
    private static readonly SeatId Witch = new(1);
    private static readonly SeatId Collector = new(9);

    /// <summary>死亡但有重获窗口的女巫：能力仍在（存活人数 &gt; 3 时诅咒继续有效）。</summary>
    [Fact]
    public void WitchCursePresence_DeadButRegained_IsPresent()
    {
        var presence = RoleContracts.AbilityPresences.Single(candidate =>
            candidate.Ability == new AbilityId("witch.curse"));
        var seats = AliveSeats();

        var dead = Fold(Witch, "witch", LifeState.Dead, AliveSeats(), withRegain: false);
        Assert.False(presence.IsPresent(new AbilityPresenceContext { State = dead, Seats = seats }));

        var regained = Fold(Witch, "witch", LifeState.Dead, AliveSeats(), withRegain: true);
        Assert.True(presence.IsPresent(new AbilityPresenceContext { State = regained, Seats = seats }));
    }

    /// <summary>
    /// 重获结束 → 女巫诅咒由存续契约在本次对账里解除（死亡触发链路之外的另一条收口：
    /// 能力失去 ⇒ 它名下的持续型效果立即终止）。
    /// </summary>
    [Fact]
    public void WitchCursePresence_TerminatesCurseWhenRegainEnds()
    {
        var curse = new PersistentEffect
        {
            Id = new EffectId("test:curse"),
            Source = Witch,
            Ability = new AbilityId("witch.curse"),
            Target = new SeatId(2),
            SourceCharacter = new CharacterId("witch"),
        };
        var regained = GameStateMachine.Apply(
            Fold(Witch, "witch", LifeState.Dead, AliveSeats(), withRegain: true),
            new PersistentEffectAppliedEvent { Effect = curse });

        var context = new SettlementContext
        {
            State = regained,
            Seats = AliveSeats(),
            Abilities = NightActions.Resolutions,
            AbilityPresences = RoleContracts.AbilityPresences,
        };

        Assert.DoesNotContain(
            SettlementReconciler.Reconcile(context).Events,
            gameEvent => gameEvent is PersistentEffectTerminatedEvent terminated && terminated.EffectId == curse.Id);

        // 重获窗口终止（下个黄昏）：诅咒随「被重获的能力再次失去」立即终止——折叠链路收口（R-0054 第 3 条）；
        // 存续契约是第二道保险（能力不在 ⇒ 诅咒解除，R-0052 / R-0054）。
        var window = regained.PersistentEffects.Single(effect => effect.Window == EffectWindowKind.RegainedAbility);
        var expired = GameStateMachine.Apply(regained, new PersistentEffectTerminatedEvent
        {
            EffectId = window.Id,
            Termination = new EffectTermination
            {
                Kind = EffectTerminationKind.NoLongerApplies,
                Reason = "测试：下个黄昏移除重获能力标记",
            },
        });

        var presence = RoleContracts.AbilityPresences.Single(candidate =>
            candidate.Ability == new AbilityId("witch.curse"));
        Assert.True(expired.PersistentEffects.Single(effect => effect.Id == curse.Id).IsTerminated);
        Assert.False(presence.IsPresent(new AbilityPresenceContext
        {
            State = expired,
            Seats = AliveSeats(),
        }));
    }

    /// <summary>死亡但有重获窗口的诺-达鲺：常驻中毒的期望集照常产出。</summary>
    [Fact]
    public void NoDashiiPoison_DeadButRegained_StillPoisons()
    {
        var source = NightActions.StandingEffects.Single(candidate =>
            candidate.Ability == new AbilityId("no-dashii.poison"));
        var seats = new[] { new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4) };

        var dead = Fold(new SeatId(1), "no-dashii", LifeState.Dead, seats, withRegain: false);
        var without = source.Evaluate(new StandingEffectContext { State = dead, Seats = seats });
        Assert.True(without.IsConclusive);
        Assert.Empty(without.Expectations);

        var regained = Fold(new SeatId(1), "no-dashii", LifeState.Dead, seats, withRegain: true);
        var with = source.Evaluate(new StandingEffectContext { State = regained, Seats = seats });
        Assert.True(with.IsConclusive);
        Assert.Equal(2, with.Expectations.Count);
    }

    /// <summary>死亡但有重获窗口的涡流：镇民信息能力的必假约束照常进失效账本。</summary>
    [Fact]
    public void VortoxInterference_DeadButRegained_StillForcesFalseInfo()
    {
        var clockmaker = NightActions.Resolutions.Find(new CharacterId("clockmaker"))
            ?? throw new InvalidOperationException("钟表匠没有注册结算契约");
        var seats = new[] { new SeatId(1), new SeatId(2), Collector };

        var dead = Fold(new SeatId(1), "vortox", LifeState.Dead, seats, withRegain: false);
        Assert.Empty(clockmaker.InterferenceMalfunctions(ContextFor(dead, seats)));

        var regained = Fold(new SeatId(1), "vortox", LifeState.Dead, seats, withRegain: true);
        Assert.Equal([MalfunctionKind.Vortox], clockmaker.InterferenceMalfunctions(ContextFor(regained, seats)));
    }

    private static AbilityResolutionContext ContextFor(GameState state, IReadOnlyList<SeatId> seats) => new()
    {
        SlotId = new StepSlotId("clockmaker"),
        PlanLabel = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Actor = new SeatId(2),
        ActorCharacter = new CharacterId("clockmaker"),
        ActorOwnCharacter = new CharacterId("clockmaker"),
        Seats = seats,
        State = state,
        Outcome = new AbilityOutcome { Effective = true },
        DaysStarted = 1,
    };

    private static SeatId[] AliveSeats() =>
        [Witch, new SeatId(2), new SeatId(3), new SeatId(4), Collector];

    /// <summary>构造「目标席位死亡（可选重获窗口）+ 其余席位存活」的账。</summary>
    private static GameState Fold(
        SeatId target,
        string targetCharacter,
        LifeState targetLife,
        IReadOnlyList<SeatId> seats,
        bool withRegain)
    {
        var events = new List<GameEvent>();
        foreach (var seat in seats)
        {
            var character = seat == target
                ? targetCharacter
                : seat == Collector ? "bone-collector" : "clockmaker";
            events.Add(new SeatStateChangedEvent
            {
                Seat = seat,
                Character = new CharacterId(character),
                Alignment = Alignment.Good,
                Life = seat == target ? targetLife : LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            });
        }

        if (!seats.Contains(target))
        {
            events.Add(new SeatStateChangedEvent
            {
                Seat = target,
                Character = new CharacterId(targetCharacter),
                Alignment = Alignment.Good,
                Life = targetLife,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            });
        }

        if (withRegain)
        {
            events.Add(new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId($"test:regain:{target.Value}"),
                    Source = Collector,
                    Ability = new AbilityId("bone-collector.regain"),
                    Target = target,
                    SourceCharacter = new CharacterId("bone-collector"),
                    GrantedCharacter = new CharacterId(targetCharacter),
                    Window = EffectWindowKind.RegainedAbility,
                    SourceStateIndependent = true,
                },
            });
        }

        return GameStateMachine.Fold(events);
    }
}
