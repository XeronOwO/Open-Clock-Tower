using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 女巫三种口径的规则用例（走引擎使用的公开目录检索，与生产同一批实现）：
/// 夜晚施加诅咒（提示 / 结算）、白天提名即死（事件触发）、只剩三名存活时失去能力（能力存续）。
/// </summary>
/// <remarks>
/// 来源：百科《女巫》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记；
/// 挂起语义见 <c>docs/standard/rulings.md</c> R-0012；"任意玩家"含自己与已死亡玩家见
/// 《重要细节》三-1 · 2026-10-01 抓取。
/// </remarks>
public sealed class WitchAbilitiesTests
{
    private static readonly CharacterId Witch = new("witch");
    private static readonly AbilityId CurseAbility = new("witch.curse");
    private static readonly EffectId CurseId = new("sv:night-2:witch:curse");

    private static INightAction Prompt => NightActions.Default.Find(Witch)!;

    private static IAbilityResolution Resolution => NightActions.Resolutions.Find(Witch)!;

    private static IEventTrigger Trigger => Assert.Single(RoleContracts.EventTriggers);

    private static IAbilityPresence Presence => Assert.Single(RoleContracts.AbilityPresences);

    /// <summary>目录登记：女巫的提示与结算同时可用（建表能开夜、结算找得到人）。</summary>
    [Fact]
    public void Catalog_CoversWitchForBothPromptAndResolution()
    {
        Assert.Equal(Witch, Prompt.Character);
        Assert.Equal(Witch, Resolution.Character);
        Assert.Equal(new AbilityId("witch"), Resolution.Ability);
        Assert.Equal(CurseAbility, Trigger.Ability);
        Assert.Equal(CurseAbility, Presence.Ability);
    }

    // ---- 夜晚提示 ----

    [Fact]
    public void Prompt_OffersEverySeatIncludingSelfAndDeadPlayers()
    {
        var state = Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii"));

        var prompt = Prompt.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = SeatsOf(state),
            State = state,
        });

        Assert.Equal(["seat:1", "seat:2", "seat:3", "seat:4"], prompt.Options.Select(option => option.Value));
        Assert.Equal(NoOptionBehavior.BlockAndAlert, prompt.OnNoOption);
        Assert.Contains("诅咒", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>只剩三名存活玩家（此处 4 席中死去 1 席）→ 失去能力：空选项 + Skip，不把女巫叫起来。</summary>
    [Fact]
    public void Prompt_WhenAbilityLost_SkipsWithoutAnyOption()
    {
        var state = Killed(
            Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii")),
            seat: 4);

        var prompt = Prompt.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = SeatsOf(state),
            State = state,
        });

        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.Skip, prompt.OnNoOption);
        Assert.Contains("失去", prompt.Context, StringComparison.Ordinal);
        Assert.Equal(DecisionPointOutcome.Skipped, prompt.Evaluate());
    }

    [Fact]
    public void Prompt_WhenWitchIsDead_SkipsWithoutAnyOption()
    {
        var state = Killed(
            Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii")),
            seat: 1);

        var prompt = Prompt.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = SeatsOf(state),
            State = state,
        });

        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.Skip, prompt.OnNoOption);
    }

    // ---- 夜晚结算 ----

    [Fact]
    public void Resolve_Effective_AppliesCurseWithoutDimension()
    {
        var events = Resolution.Resolve(Context(choice: "seat:2", effective: true));

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(CurseId, applied.Effect.Id);
        Assert.Equal(new SeatId(1), applied.Effect.Source);
        Assert.Equal(new SeatId(2), applied.Effect.Target);
        Assert.Equal(CurseAbility, applied.Effect.Ability);
        Assert.Equal(Witch, applied.Effect.SourceCharacter);
        Assert.Null(applied.Effect.Dimension);
    }

    /// <summary>中毒 / 醉酒 / 死亡：能力不生效 → 按提示标记的放置条件，此时不放置「被诅咒」。</summary>
    [Fact]
    public void Resolve_Ineffective_PlacesNoCurse()
    {
        Assert.Empty(Resolution.Resolve(Context(choice: "seat:2", effective: false)));
    }

    /// <summary>计划建好之后、结算之前存活人数掉到三名：能力已失去，不落任何效果。</summary>
    [Fact]
    public void Resolve_AbilityLostBeforeSettlement_PlacesNoCurse()
    {
        var context = Context(choice: "seat:2", effective: true) with
        {
            State = Killed(Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii")), seat: 4),
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4)],
        };

        Assert.Empty(Resolution.Resolve(context));
    }

    [Fact]
    public void Resolve_IllegalChoice_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => Resolution.Resolve(Context(choice: "随便", effective: true)));
    }

    // ---- 白天：提名即死 ----

    [Fact]
    public void Trigger_CursedNominatorDies_NominationItselfIsUntouched()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);

        var events = Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1)));

        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Equal(new SeatId(1), death.CausedBy);
        Assert.Equal(CurseId, death.EffectId);
        Assert.Contains("女巫", death.Reason, StringComparison.Ordinal);

        // 触发器只产出死亡事实：提名事件由白天机器产出，不在这里重复，也不产生处决。
        Assert.Empty(events.OfType<ExecutedEvent>());
        Assert.DoesNotContain(events, gameEvent => gameEvent is NominationMadeEvent);
    }

    [Fact]
    public void Trigger_UncursedNominator_ProducesNothing()
    {
        var state = FourSeats();

        Assert.Empty(Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1))));
    }

    /// <summary>来源醉酒 → 诅咒**挂起**（R-0012）：这一刻不生效，也不终止。</summary>
    [Fact]
    public void Trigger_SuspendedCurse_DoesNotKill()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);
        state = GameStateMachine.Apply(state, new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Drunk = DrunkState.Drunk,
            Reason = "test.drunk",
        });

        Assert.Empty(Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1))));
        Assert.False(state.PersistentEffects.Single().IsTerminated);
    }

    /// <summary>来源恢复清醒 → 挂起的诅咒**继续生效**（同一效果、不是重新施加，R-0012）。</summary>
    [Fact]
    public void Trigger_CurseResumesAfterTheSourceSoberUp()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);
        state = GameStateMachine.Apply(state, new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Drunk = DrunkState.Drunk,
            Reason = "test.drunk",
        });
        Assert.Empty(Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1))));

        state = GameStateMachine.Apply(state, new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Drunk = DrunkState.Sober,
            Reason = "test.sober",
        });
        var events = Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1)));

        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);

        // 还是同一条效果：挂起不是终止，恢复也不是重新施加（R-0012）。
        var curse = Assert.Single(state.PersistentEffects);
        Assert.Equal(CurseId, curse.Id);
        Assert.False(curse.IsTerminated);
    }

    /// <summary>幂等：同一条提名事件被喂第二次（后果已折进账）不再产出第二条死亡事实。</summary>
    [Fact]
    public void Trigger_RepeatedEvaluationOfTheSameNomination_ProducesNothingTwice()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);
        var nomination = Nomination(nominator: 2, nominee: 1);

        var first = Trigger.Evaluate(TriggerContext(state, nomination));
        Assert.Single(first.OfType<SeatStateChangedEvent>());

        var folded = first.Aggregate(state, GameStateMachine.Apply);
        Assert.Empty(Trigger.Evaluate(TriggerContext(folded, nomination)));
    }

    /// <summary>
    /// 同批里既施加诅咒又结束白天（现实流程不可达，但契约必须不炸）：账里那条效果先折进来，
    /// 触发器的终止事件随之产出——顺序折叠成立，不会出现「终止一条不存在的效果」。
    /// </summary>
    [Fact]
    public void Trigger_TerminationInTheSameBatchAsApplication_FoldsInOrder()
    {
        var state = FourSeats();
        var applied = new PersistentEffectAppliedEvent
        {
            Effect = new PersistentEffect
            {
                Id = CurseId,
                Source = new SeatId(1),
                Ability = CurseAbility,
                Target = new SeatId(2),
                SourceCharacter = Witch,
            },
        };

        var folded = GameStateMachine.Apply(state, applied);
        var events = Trigger.Evaluate(TriggerContext(folded, applied, new DayClosedEvent { DayNumber = 1 }));

        var terminated = Assert.Single(events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(CurseId, terminated.EffectId);
        Assert.True(events.Aggregate(folded, GameStateMachine.Apply)
            .PersistentEffects.Single(effect => effect.Id == CurseId).IsTerminated);
    }

    /// <summary>被诅咒者已经死了（级联里的重复求值）：不再产出第二条死亡事实。</summary>
    [Fact]
    public void Trigger_AlreadyDeadNominator_DoesNotDieTwice()
    {
        var state = Cursed(
            Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii"), (5, "sage")),
            source: 1,
            target: 2);
        state = Killed(state, seat: 2);

        Assert.Empty(Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 1))));
    }

    /// <summary>存活 ≤3 时能力已失去：提名不再致死（诅咒由存续契约解除）。</summary>
    [Fact]
    public void Trigger_WhenAbilityLost_DoesNotKill()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);
        state = Killed(state, seat: 4);

        Assert.Empty(Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 2, nominee: 3))));
    }

    /// <summary>黄昏：诅咒只活一个白天，白天一结束就按提示标记的移除时机撤下。</summary>
    [Fact]
    public void Trigger_DayClosed_TerminatesLiveCurse()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);

        var events = Trigger.Evaluate(TriggerContext(state, new DayClosedEvent { DayNumber = 1 }));

        var terminated = Assert.Single(events.OfType<PersistentEffectTerminatedEvent>());
        Assert.Equal(CurseId, terminated.EffectId);
        Assert.Equal(EffectTerminationKind.NoLongerApplies, terminated.Termination.Kind);
        Assert.Contains("黄昏", terminated.Termination.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Trigger_DayClosed_LeavesAlreadyTerminatedCurseAlone()
    {
        var state = Cursed(FourSeats(), source: 1, target: 2);
        state = GameStateMachine.Apply(state, new PersistentEffectTerminatedEvent
        {
            EffectId = CurseId,
            Termination = new EffectTermination
            {
                Kind = EffectTerminationKind.StorytellerVoided,
                Reason = "测试：说书人强制作废",
            },
        });

        Assert.Empty(Trigger.Evaluate(TriggerContext(state, new DayClosedEvent { DayNumber = 1 })));
    }

    /// <summary>女巫死亡：诅咒由既有折叠链路终止（死亡即失去角色能力）。</summary>
    [Fact]
    public void Curse_IsTerminatedWhenTheWitchDies()
    {
        var state = Killed(Cursed(FourSeats(), source: 1, target: 2), seat: 1);

        var curse = Assert.Single(state.PersistentEffects);
        Assert.True(curse.IsTerminated);
        Assert.Equal(EffectTerminationKind.SourceDied, curse.Termination!.Kind);
    }

    /// <summary>百科范例 2：女巫诅咒自己，自己发起提名 → 自己死亡。</summary>
    [Fact]
    public void Trigger_WitchCursingHerself_DiesOnHerOwnNomination()
    {
        var state = Cursed(FourSeats(), source: 1, target: 1);

        var events = Trigger.Evaluate(TriggerContext(state, Nomination(nominator: 1, nominee: 2)));

        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(1), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
    }

    // ---- 能力存续 ----

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, false)]
    [InlineData(2, false)]
    public void Presence_AliveCountBoundary(int aliveCount, bool expected)
    {
        var state = Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii"));
        for (var seat = 4; seat > aliveCount; seat--)
        {
            state = Killed(state, seat);
        }

        Assert.Equal(expected, Presence.IsPresent(new AbilityPresenceContext
        {
            State = state,
            Seats = SeatsOf(state),
        }));
    }

    [Fact]
    public void Presence_WithoutWitch_IsNotPresent()
    {
        var state = Assigned((1, "dreamer"), (2, "clockmaker"));

        Assert.False(Presence.IsPresent(new AbilityPresenceContext
        {
            State = state,
            Seats = SeatsOf(state),
        }));
    }

    /// <summary>生死未观测 → 判定不了（null）：不解除任何效果（解除是破坏性动作，必须有据）。</summary>
    [Fact]
    public void Presence_UnobservedLife_IsIndeterminate()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = Witch,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.assignment",
            },
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("dreamer"),
                Reason = "test.assignment",
            },
        ]);

        Assert.Null(Presence.IsPresent(new AbilityPresenceContext
        {
            State = state,
            Seats = SeatsOf(state),
        }));
    }

    // ---- 夹具 ----

    private static IReadOnlyList<SeatId> SeatsOf(GameState state) =>
        [.. state.Seats.Select(entry => entry.Seat)];

    /// <summary>
    /// 四席（存活 4 &gt; 3）：女巫保住能力的最小局面——「只剩三名存活玩家时你失去此能力」
    /// 是这条规则的边界，三席局里女巫根本不该有诅咒（见 <see cref="Prompt_WhenAbilityLost_SkipsWithoutAnyOption"/>）。
    /// </summary>
    private static GameState FourSeats() =>
        Assigned((1, "witch"), (2, "dreamer"), (3, "clockmaker"), (4, "no-dashii"));

    private static GameState Assigned(params (int Seat, string Character)[] seats) =>
        GameStateMachine.Fold(
        [
            .. seats.Select(seat => new SeatStateChangedEvent
            {
                Seat = new SeatId(seat.Seat),
                Character = new CharacterId(seat.Character),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.assignment",
            }),
        ]);

    private static GameState Killed(GameState state, int seat) =>
        GameStateMachine.Apply(state, new SeatStateChangedEvent
        {
            Seat = new SeatId(seat),
            Life = LifeState.Dead,
            Reason = "test.death",
        });

    private static GameState Cursed(GameState state, int source, int target) =>
        GameStateMachine.Apply(state, new PersistentEffectAppliedEvent
        {
            Effect = new PersistentEffect
            {
                Id = CurseId,
                Source = new SeatId(source),
                Ability = CurseAbility,
                Target = new SeatId(target),
                SourceCharacter = Witch,
            },
        });

    private static EventTriggerContext TriggerContext(GameState state, params GameEvent[] events) => new()
    {
        State = state,
        Seats = SeatsOf(state),
        Events = events,
    };

    private static NominationMadeEvent Nomination(int nominator, int nominee) => new()
    {
        DayNumber = 1,
        NominationIndex = 1,
        Nominator = new SeatId(nominator),
        Nominee = new SeatId(nominee),
    };

    private static AbilityResolutionContext Context(string choice, bool effective) => new()
    {
        SlotId = new StepSlotId("witch"),
        PlanLabel = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Actor = new SeatId(1),
        ActorCharacter = Witch,
        Seats = [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4)],
        State = FourSeats(),
        Outcome = new AbilityOutcome
        {
            Effective = effective,
            Malfunction = effective ? null : MalfunctionKind.Poisoned,
        },
        Choice = choice,
    };
}
