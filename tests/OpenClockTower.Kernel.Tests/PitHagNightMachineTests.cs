using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量（口径见 <c>docs/standard/rulings.md</c> R-0030）：
/// 窗口开启 / 待定死亡 / 说书人裁定 / 追加死亡 / 恶魔段收口 与关闭后的拒绝。
/// </summary>
public sealed class PitHagNightMachineTests
{
    private static readonly CharacterId PitHag = new("pit-hag");
    private static readonly CharacterId Vortox = new("vortox");
    private static readonly AbilityId VortoxKill = new("vortox");
    private static readonly AbilityId Casualty = new("pit-hag.casualty");

    /// <summary>追加死亡归因为麻脸巫婆（不触发"被恶魔杀死"类能力）。</summary>
    [Fact]
    public void Casualty_KillsWithPitHagAttribution()
    {
        var state = NightState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive), (3, "vortox", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new PitHagCasualtyInput { Target = new SeatId(2), Note = "说书人平衡局面" });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var changed = Assert.Single(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(LifeState.Dead, changed.Life);
        Assert.Equal(new SeatId(1), changed.CausedBy);
        Assert.Contains("麻脸巫婆造成死亡", changed.Reason);
    }

    /// <summary>裁定「确认」→ 目标死亡，归因为发起击杀的恶魔（不是麻脸巫婆）。</summary>
    [Fact]
    public void ResolveDeferred_Killed_DiesWithDemonAttribution()
    {
        var state = NightState(slotIndex: 1, closesAfter: 2, deferred: [(2, 3)]);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive), (3, "vortox", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is DeferredDeathResolvedEvent { Killed: true });
        var changed = Assert.Single(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(LifeState.Dead, changed.Life);
        Assert.Equal(new SeatId(3), changed.CausedBy);
        Assert.Empty(outcome.State!.PitHagNight!.Deferred);
    }

    /// <summary>裁定「阻止」→ 目标不死，只留一条裁定（免死）。</summary>
    [Fact]
    public void ResolveDeferred_Prevented_LeavesTargetAlive()
    {
        var state = NightState(slotIndex: 1, closesAfter: 2, deferred: [(2, 3)]);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive), (3, "vortox", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = false });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is DeferredDeathResolvedEvent { Killed: false });
    }

    /// <summary>窗口收口：走到最后一个恶魔行动之后，未裁定的待定死亡按默认结果生效并显式记录。</summary>
    [Fact]
    public void WindowClose_UnresolvedDeathsTakeEffect_AndWindowCloses()
    {
        var state = NightState(slotIndex: 2, closesAfter: 2, deferred: [(2, 3)]);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive), (3, "vortox", LifeState.Alive));

        var outcome = StepMachine.Handle(state, Context(ledger), new SlotQuotaElapsedInput());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is DeferredDeathResolvedEvent { Killed: true, Note: var note }
                && note.Contains("窗口关闭时仍未裁定"));
        Assert.Contains(outcome.Events.OfType<SeatStateChangedEvent>(), changed => changed.Life == LifeState.Dead);
        Assert.Contains(outcome.Events, gameEvent => gameEvent is PitHagNightClosedEvent);
        Assert.Null(outcome.State!.PitHagNight);
    }

    /// <summary>
    /// 携带「转化」载荷的待定死亡：说书人确认时**不产生被攻击者的死亡**，改为
    /// 「外来者变邪恶方古 + 原方古死亡 + 限一次标记」——方古侵染；平台口径见 R-0034。
    /// </summary>
    [Fact]
    public void ResolveDeferred_Transformation_ConvertsInsteadOfKilling()
    {
        var state = TransformationState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "sweetheart", LifeState.Alive),
            (3, "fang-gu", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);

        var changes = outcome.Events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, changes.Length);

        var converted = Assert.Single(changes, change => change.Seat == new SeatId(2));
        Assert.Equal(new CharacterId("fang-gu"), converted.Character);
        Assert.Equal(Alignment.Evil, converted.Alignment);
        Assert.Null(converted.Life);

        var died = Assert.Single(changes, change => change.Seat == new SeatId(3));
        Assert.Equal(LifeState.Dead, died.Life);

        var marker = Assert.Single(outcome.Events.OfType<FangGuInfectionRecordedEvent>());
        Assert.Equal(new SeatId(2), marker.Seat);
        Assert.Equal(new SeatId(3), marker.Source);
        Assert.NotNull(outcome.State!.FangGuInfection);
        Assert.Empty(outcome.State!.PitHagNight!.Deferred);
    }

    /// <summary>
    /// 目标在确认前已经死亡 → 整体不发生（没有「成功杀死」这回事，原方古也不死）；
    /// 裁定本身仍留在事件流里，只从待定表移除。
    /// </summary>
    [Fact]
    public void ResolveDeferred_Transformation_WhenTargetAlreadyDead_SkipsConversion()
    {
        var state = TransformationState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "sweetheart", LifeState.Dead),
            (3, "fang-gu", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(outcome.Events, gameEvent => gameEvent is DeferredDeathResolvedEvent { Killed: true });
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Empty(outcome.Events.OfType<FangGuInfectionRecordedEvent>());
        Assert.Null(outcome.State!.FangGuInfection);
    }

    /// <summary>窗口收口时未裁定的转化载荷按自然结果生效：走转化而不是击杀（R-0030 第 3 条 / R-0034）。</summary>
    [Fact]
    public void WindowClose_Transformation_TakesEffectAsConversion()
    {
        var state = TransformationState(slotIndex: 2, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "sweetheart", LifeState.Alive),
            (3, "fang-gu", LifeState.Alive));

        var outcome = StepMachine.Handle(state, Context(ledger), new SlotQuotaElapsedInput());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is DeferredDeathResolvedEvent { Killed: true, Note: var note }
                && note.Contains("侵染", StringComparison.Ordinal));
        var changes = outcome.Events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Contains(changes, change => change.Seat == new SeatId(3) && change.Life == LifeState.Dead);
        Assert.Single(outcome.Events.OfType<FangGuInfectionRecordedEvent>());
        Assert.NotNull(outcome.State!.FangGuInfection);
        Assert.Null(outcome.State!.PitHagNight);
    }

    /// <summary>窗口关闭之后：追加死亡与裁定都被显式拒绝（不是静默忽略）。</summary>
    [Fact]
    public void AfterClose_BothCommandsAreRejected()
    {
        var closed = NightState(slotIndex: 2, closesAfter: 2, deferred: [(2, 3)]);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive), (3, "vortox", LifeState.Alive));
        var afterClose = StepMachine.Handle(closed, Context(ledger), new SlotQuotaElapsedInput()).State!;

        var casualty = StepMachine.Handle(
            afterClose,
            Context(ledger),
            new PitHagCasualtyInput { Target = new SeatId(2) });
        var resolve = StepMachine.Handle(
            afterClose,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Rejected, casualty.Kind);
        Assert.Equal(StepMachineRejectionReason.NoPitHagNight, casualty.RejectionReason);
        Assert.Equal(StepMachineOutcomeKind.Rejected, resolve.Kind);
        Assert.Equal(StepMachineRejectionReason.NoPitHagNight, resolve.RejectionReason);
    }

    /// <summary>同一夜不能开两次窗口：重复开窗是事件流损坏，显式抛错。</summary>
    [Fact]
    public void OpeningTwice_Throws()
    {
        var state = NightState(slotIndex: 1, closesAfter: 2);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            state,
            new PitHagNightOpenedEvent
            {
                Source = new SeatId(1),
                ClosesAfterSlotIndex = 2,
                CasualtyAbility = Casualty,
            }));
    }

    /// <summary>没有待定死亡却去裁定 → 显式拒绝。</summary>
    [Fact]
    public void ResolveWithoutDeferred_IsRejected()
    {
        var state = NightState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger((1, "pit-hag", LifeState.Alive), (2, "clockmaker", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(StepMachineRejectionReason.UnexpectedInput, outcome.RejectionReason);
    }

    private static StepMachineState NightState(
        int slotIndex,
        int closesAfter,
        params (int Target, int Source)[] deferred)
    {
        var started = StepMachine.StartPhase(Plan(closesAfter), previous: null, GameState.Empty);
        var state = StepMachine.Apply(
            started.State,
            new PitHagNightOpenedEvent
            {
                Source = new SeatId(1),
                ClosesAfterSlotIndex = closesAfter,
                CasualtyAbility = Casualty,
            })!;

        state = state with { SlotIndex = slotIndex };

        foreach (var (target, source) in deferred)
        {
            state = StepMachine.Apply(
                state,
                new DeferredDeathRecordedEvent
                {
                    Target = new SeatId(target),
                    Source = new SeatId(source),
                    Ability = VortoxKill,
                    Note = "涡流夜间击杀（麻脸巫婆之夜：死亡待说书人裁定）",
                })!;
        }

        return state;
    }

    private static StepPlan Plan(int demonSlot) => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Empty(new StepSlotId("pit-hag"), PitHag),
            StepSlot.Empty(new StepSlotId("vortox"), Vortox),
            StepSlot.Beat(new StepSlotId("dawn")),
        ],
    };

    /// <summary>带一条「方古侵染」转化载荷的窗口状态（目标 2 号外来者、来源 3 号方古）。</summary>
    private static StepMachineState TransformationState(int slotIndex, int closesAfter)
    {
        var state = NightState(slotIndex, closesAfter);
        return StepMachine.Apply(
            state,
            new DeferredDeathRecordedEvent
            {
                Target = new SeatId(2),
                Source = new SeatId(3),
                Ability = new AbilityId("fang-gu"),
                Note = "方古夜间击杀（首次命中外来者：说书人确认则按侵染结算）",
                Transformation = new DeferredTransformation
                {
                    Target = new SeatId(2),
                    Character = new CharacterId("fang-gu"),
                    Alignment = Alignment.Evil,
                    Dies = new SeatId(3),
                    Note = "外来者（2 号）变成新的邪恶方古，原方古（3 号）死亡",
                },
            })!;
    }

    /// <summary>
    /// 携带「保留能力」载荷的待定死亡（亡骨魔杀爪牙，R-0056）：说书人确认时**先落窗口与击杀事实、
    /// 再落死亡**——顺序有语义（死亡折叠时据此判定"没有失去能力"）；阻止时整条不产生。
    /// </summary>
    [Fact]
    public void ResolveDeferred_Retention_LandsWindowAndRecordBeforeTheDeath()
    {
        var state = RetentionState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "vigormortis", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = true });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);

        var events = outcome.Events.ToArray();
        var retainIndex = Array.FindIndex(events, gameEvent => gameEvent is PersistentEffectAppliedEvent);
        var recordIndex = Array.FindIndex(events, gameEvent => gameEvent is VigormortisKillRecordedEvent);
        var deathIndex = Array.FindIndex(events, gameEvent =>
            gameEvent is SeatStateChangedEvent { Seat: var seat, Life: LifeState.Dead } && seat == new SeatId(2));
        Assert.True(retainIndex >= 0 && recordIndex >= 0 && deathIndex >= 0);
        Assert.True(retainIndex < deathIndex, "「保留能力」窗口必须排在死亡之前");
        Assert.True(recordIndex < deathIndex, "击杀事实必须排在死亡之前");

        var window = ((PersistentEffectAppliedEvent)events[retainIndex]).Effect;
        Assert.Equal(EffectWindowKind.RetainedAbility, window.Window);
        Assert.Equal(new SeatId(3), window.Source);
        Assert.Equal(new SeatId(2), window.Target);

        var recorded = (VigormortisKillRecordedEvent)events[recordIndex];
        Assert.Equal(new SeatId(3), recorded.Demon);
        Assert.Equal(new SeatId(2), recorded.Minion);
        Assert.Equal(SeatRingDirection.CounterClockwise, recorded.Side);
    }

    /// <summary>阻止死亡 → 保留能力与击杀事实都不产生（保护的是"确认"与"自然结果"两条路径的一致）。</summary>
    [Fact]
    public void ResolveDeferred_Retention_PreventedDeathLandsNothing()
    {
        var state = RetentionState(slotIndex: 1, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "vigormortis", LifeState.Alive));

        var outcome = StepMachine.Handle(
            state,
            Context(ledger),
            new ResolveDeferredDeathInput { Target = new SeatId(2), Killed = false });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Empty(outcome.Events.OfType<PersistentEffectAppliedEvent>());
        Assert.Empty(outcome.Events.OfType<VigormortisKillRecordedEvent>());
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
    }

    /// <summary>窗口收口时未裁定的保留载荷按自然结果生效：窗口 + 事实 + 死亡一起落地。</summary>
    [Fact]
    public void WindowClose_Retention_TakesEffectOnConfirm()
    {
        var state = RetentionState(slotIndex: 2, closesAfter: 2);
        var ledger = Ledger(
            (1, "pit-hag", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "vigormortis", LifeState.Alive));

        var outcome = StepMachine.Handle(state, Context(ledger), new SlotQuotaElapsedInput());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Single(outcome.Events.OfType<PersistentEffectAppliedEvent>());
        Assert.Single(outcome.Events.OfType<VigormortisKillRecordedEvent>());
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is SeatStateChangedEvent { Seat: var seat, Life: LifeState.Dead }
                && seat == new SeatId(2));
    }

    /// <summary>带一条「保留能力」载荷的窗口状态（目标 2 号爪牙、来源 3 号亡骨魔）。</summary>
    private static StepMachineState RetentionState(int slotIndex, int closesAfter)
    {
        var state = NightState(slotIndex, closesAfter);
        return StepMachine.Apply(
            state,
            new DeferredDeathRecordedEvent
            {
                Target = new SeatId(2),
                Source = new SeatId(3),
                Ability = new AbilityId("vigormortis"),
                Note = "亡骨魔夜间击杀（麻脸巫婆之夜：死亡待说书人裁定）",
                Retention = new DeferredRetention
                {
                    RetainEffect = new PersistentEffect
                    {
                        Id = new EffectId("standing:vigormortis.retention:3:2"),
                        Source = new SeatId(3),
                        Ability = new AbilityId("vigormortis.retention"),
                        Target = new SeatId(2),
                        SourceCharacter = new CharacterId("vigormortis"),
                        Window = EffectWindowKind.RetainedAbility,
                    },
                    Side = SeatRingDirection.CounterClockwise,
                },
            })!;
    }

    private static SettlementContext Context(GameState ledger) =>
        new()
        {
            State = ledger,
            Seats = [.. ledger.Seats.Select(entry => entry.Seat)],
            Abilities = EmptyAbilities.Instance,
        };

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Life = Fact(row.Life),
                    Alignment = Fact(Alignment.Evil),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };

    private sealed class EmptyAbilities : IAbilityResolutionCatalog
    {
        internal static readonly EmptyAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
