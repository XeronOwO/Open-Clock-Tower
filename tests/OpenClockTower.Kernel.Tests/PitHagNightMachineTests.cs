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
