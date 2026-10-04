using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 屠夫「额外提名窗口」的内核行为（票据 `traveller-and-exile` · D4 / R-0050）：开窗时机、额外提名的受理与
/// 票数口径、二次处决与边界，以及折叠层对损坏流的显式失败。
/// </summary>
/// <remarks>
/// 内核不认识角色：这里用脚本化的假窗口来源覆盖「可用 / 不可用 / 判定不了」；
/// 屠夫来源本身（存活 + 能力生效）见规则层 <c>ButcherExtraNominationSourceTests</c>。
/// </remarks>
public sealed class ButcherWindowTests
{
    [Fact]
    public void FirstExecution_WithoutSources_ClosesDayDirectly()
    {
        var context = Context(seatCount: 3);
        var counted = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2]);
        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);

        var closed = DayPhaseFixture.Close(counted.State, context);

        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Single(closed.Events.OfType<ExecutedEvent>());
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExtraNominationWindowOpenedEvent);
        Assert.True(closed.State.IsPlanCompleted);
        Assert.Single(closed.State.Day!.Days[^1].Executions);
    }

    [Fact]
    public void FirstExecution_WithUnavailableSource_ClosesDayDirectly()
    {
        var context = WithSource(ExtraNominationOutcome.Unavailable, seat: 3, seatCount: 3);
        var counted = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2]);

        var closed = DayPhaseFixture.Close(counted.State, context);

        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExtraNominationWindowOpenedEvent);
        Assert.Null(closed.State.Day!.Days[^1].ExtraNomination);
    }

    [Fact]
    public void FirstExecution_WithIndeterminateSource_RejectsAndLeavesStateUntouched()
    {
        var context = WithSource(ExtraNominationOutcome.Indeterminate, seat: 3, seatCount: 3);
        var counted = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2]);

        var closed = DayPhaseFixture.Close(counted.State, context);

        Assert.Equal(StepMachineOutcomeKind.Rejected, closed.Kind);
        Assert.Equal("day.extra_nomination_indeterminate", closed.RejectionCode);
        Assert.Empty(closed.Events);
        Assert.Same(counted.State, closed.State);
    }

    [Fact]
    public void FirstExecution_WithAvailableSource_OpensWindow_AndDayStaysOpen()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);
        var counted = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2]);

        var opened = DayPhaseFixture.Close(counted.State, context);

        Assert.Equal(StepMachineOutcomeKind.Applied, opened.Kind);
        Assert.Single(opened.Events.OfType<ExecutedEvent>());
        var window = Assert.Single(opened.Events.OfType<ExtraNominationWindowOpenedEvent>());
        Assert.Equal(new SeatId(4), window.Seat);
        Assert.DoesNotContain(opened.Events, item => item is DayClosedEvent);
        Assert.False(opened.State.IsPlanCompleted);
        Assert.Equal(StepSlotKind.DayWindow, opened.State.CurrentSlot!.Kind);

        var day = opened.State.Day!.OpenDay!;
        Assert.Equal(ExtraNominationWindowStatus.Open, day.ExtraNomination!.Status);
        Assert.Equal(new SeatId(4), day.ExtraNomination.Seat);
        Assert.Single(day.Executions);
    }

    [Fact]
    public void NoExecution_ClosesDayWithoutAskingForAWindow()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);

        var closed = DayPhaseFixture.Close(DayPhaseFixture.StartDay(), context);

        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExtraNominationWindowOpenedEvent);
        Assert.True(closed.State.IsPlanCompleted);
        Assert.Null(closed.State.Day!.Days[^1].ExtraNomination);
    }

    [Fact]
    public void ExtraNomination_RequiresAnOpenWindow_AndTheGrantedSeat()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);
        var started = DayPhaseFixture.StartDay();

        // 还没处决：没有窗口。
        var tooEarly = DayPhaseFixture.Apply(
            started,
            context,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(1) });
        Assert.Equal("day.extra_nomination_window_not_open", tooEarly.RejectionCode);

        var opened = OpenWindow(context);

        // 窗口开着，但不是授予席位（4 号）本人发起。
        var notGranted = DayPhaseFixture.Apply(
            opened.State,
            context,
            new NominateExtraInput { Nominator = new SeatId(1), Nominee = new SeatId(3) });
        Assert.Equal("day.extra_nomination_not_granted", notGranted.RejectionCode);

        var extra = DayPhaseFixture.Apply(
            opened.State,
            context,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(3) });
        Assert.Equal(StepMachineOutcomeKind.Applied, extra.Kind);
        var made = Assert.Single(extra.Events.OfType<ExtraNominationMadeEvent>());
        Assert.Equal(new SeatId(4), made.Nominator);
        Assert.Equal(new SeatId(3), made.Nominee);
        Assert.Equal(2, made.NominationIndex);
        Assert.Equal(NominationKind.Extra, extra.State.Day!.OpenDay!.Nominations[1].Kind);
        Assert.Equal(
            ExtraNominationWindowStatus.Used,
            extra.State.Day!.OpenDay!.ExtraNomination!.Status);

        // 先结算掉这条额外提名（窗口已用），再试第二次：同一项还在投票中时会先撞上
        // day.nomination_in_progress，窗口已用要在计票之后才看得出来。
        var settled = DayPhaseFixture.SweepAndCount(extra.State, context, index: 2, raised: [1, 2]);
        Assert.Equal(StepMachineOutcomeKind.Applied, settled.Kind);

        var reused = DayPhaseFixture.Apply(
            settled.State,
            context,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(2) });
        Assert.Equal("day.extra_nomination_window_not_open", reused.RejectionCode);
    }

    [Fact]
    public void ExtraNomination_MayReNominate_AndIgnoresTheButchersDailyNomination()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 3, seatCount: 4);

        // 白天常规提名两轮：4 号先提 2 号（2 票），3 号（屠夫）再提 1 号（3 票）→ 即将处决 1 号。
        var first = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 4, nominee: 2, raised: [1, 3]);
        var second = NominateAndCount(first.State, context, nominator: 3, nominee: 1, raised: [2, 3, 4]);
        var counted = Assert.Single(second.Events.OfType<VoteCountedEvent>());
        Assert.Equal(new SeatId(1), counted.AboutToBeExecuted);

        var opened = DayPhaseFixture.Close(second.State, context);
        var window = Assert.Single(opened.Events.OfType<ExtraNominationWindowOpenedEvent>());
        Assert.Equal(new SeatId(3), window.Seat);

        // 屠夫已经提名过（提过 1 号），目标 2 号也已经被提名过——两条当日限制都被《屠夫》明文豁免。
        var extra = DayPhaseFixture.Apply(
            opened.State,
            context,
            new NominateExtraInput { Nominator = new SeatId(3), Nominee = new SeatId(2) });

        Assert.Equal(StepMachineOutcomeKind.Applied, extra.Kind);
        var made = Assert.Single(extra.Events.OfType<ExtraNominationMadeEvent>());
        Assert.Equal(new SeatId(2), made.Nominee);
        Assert.Equal(3, made.NominationIndex);
    }

    [Fact]
    public void ExtraNominationVote_OnlyNeedsHalfAlive_NotMoreThanEarlierVotes()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);

        // 常规提名拿 3 票并被执行（窗口授予 4 号）。
        var first = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2, 3]);
        var opened = DayPhaseFixture.Close(first.State, context);
        Assert.Contains(opened.Events, item => item is ExtraNominationWindowOpenedEvent);

        var extra = DayPhaseFixture.Apply(
            opened.State,
            context,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(3) });
        Assert.Equal(StepMachineOutcomeKind.Applied, extra.Kind);

        // 额外提名只拿 2 票（4 人存活的一半），没有超过此前的 3 票——按《屠夫》仍应落靶（R-0050 第 4 条）。
        var counted = DayPhaseFixture.SweepAndCount(extra.State, context, index: 2, raised: [1, 4]);
        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        var voteCounted = Assert.Single(counted.Events.OfType<VoteCountedEvent>());
        Assert.Equal(new SeatId(3), voteCounted.AboutToBeExecuted);

        // 第二次 CloseDay：执行第二位、直接关账，不再开第二个窗口。
        var closed = DayPhaseFixture.Close(counted.State, context);
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        var executed = Assert.Single(closed.Events.OfType<ExecutedEvent>());
        Assert.Equal(new SeatId(3), executed.Seat);
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExtraNominationWindowOpenedEvent);
        Assert.True(closed.State.IsPlanCompleted);

        var day = closed.State.Day!.Days[^1];
        Assert.Equal(2, day.Executions.Count);
        Assert.Equal(new SeatId(3), day.Executions[1].Seat);
        Assert.Equal(ExecutionKind.Day, day.Executions[1].Kind);
    }

    [Fact]
    public void SecondCloseDay_WithoutUsingTheWindow_ClosesDirectly()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);
        var opened = OpenWindow(context);

        var closed = DayPhaseFixture.Close(opened.State, context);

        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExtraNominationWindowOpenedEvent);
        Assert.True(closed.State.IsPlanCompleted);
        Assert.Single(closed.State.Day!.Days[^1].Executions);
    }

    [Fact]
    public void NormalNomination_DuringTheWindow_IsRejected()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);
        var opened = OpenWindow(context);

        var rejected = DayPhaseFixture.Nominate(opened.State, context, nominator: 1, nominee: 2);

        Assert.Equal("day.nomination_window_closed", rejected.RejectionCode);
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void Fold_RejectsASecondExecution_WithoutAUsedWindow()
    {
        var state = StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day })!;

        var exception = Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            state,
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(3), Kind = ExecutionKind.Day }));

        Assert.Contains("R-0050", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fold_RejectsAWindow_WithoutAFirstExecution()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExtraNominationWindowOpenedEvent { DayNumber = 1, Seat = new SeatId(2) }));

        Assert.Contains("R-0050", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fold_RejectsAnExtraNomination_WithoutAnOpenWindow()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExtraNominationMadeEvent
            {
                DayNumber = 1,
                NominationIndex = 1,
                Nominator = new SeatId(2),
                Nominee = new SeatId(3),
            }));

        Assert.Contains("R-0050", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fold_RejectsADuplicateWindow()
    {
        var executed = StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day })!;
        var opened = StepMachine.Apply(
            executed,
            new ExtraNominationWindowOpenedEvent { DayNumber = 1, Seat = new SeatId(3) })!;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            opened,
            new ExtraNominationWindowOpenedEvent { DayNumber = 1, Seat = new SeatId(3) }));
    }

    /// <summary>
    /// 集骨者「重获能力」（R-0054）：已死亡的屠夫在重获窗口内仍可发起额外提名——百科《集骨者》范例：
    /// 「在晚上，集骨者选择了已死亡的屠夫。在接下来的白天，当一名玩家被处决后，说书人告诉屠夫
    /// 可以再进行一次处决提名」；没有窗口时按既有口径拒绝（只有存活玩家可以发起提名）。
    /// </summary>
    [Fact]
    public void ExtraNomination_AllowsADeadButRegainedButcher()
    {
        var context = WithSource(ExtraNominationOutcome.Available, seat: 4, seatCount: 4);
        var opened = OpenWindow(context);

        var dead = context with
        {
            State = GameStateMachine.Apply(context.State, new SeatStateChangedEvent
            {
                Seat = new SeatId(4),
                Life = LifeState.Dead,
                Reason = "测试：屠夫死亡",
            }),
        };

        var without = DayPhaseFixture.Apply(
            opened.State,
            dead,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(3) });
        Assert.Equal("day.nominator_dead", without.RejectionCode);

        var regained = dead with
        {
            State = GameStateMachine.Apply(dead.State, new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId("test:regain:4"),
                    Source = new SeatId(1),
                    Ability = new AbilityId("bone-collector.regain"),
                    Target = new SeatId(4),
                    SourceCharacter = new CharacterId("bone-collector"),
                    GrantedCharacter = new CharacterId("clockmaker"),
                    Window = EffectWindowKind.RegainedAbility,
                    SourceStateIndependent = true,
                },
            }),
        };

        var with = DayPhaseFixture.Apply(
            opened.State,
            regained,
            new NominateExtraInput { Nominator = new SeatId(4), Nominee = new SeatId(3) });

        Assert.Equal(StepMachineOutcomeKind.Applied, with.Kind);
        Assert.Single(with.Events.OfType<ExtraNominationMadeEvent>());
    }

    /// <summary>开一个已打开窗口的状态：4 席、1 号提 2 号并计 2 票、4 号是窗口授予席位。</summary>
    private static StepMachineOutcome OpenWindow(SettlementContext context)
    {
        var counted = NominateAndCount(DayPhaseFixture.StartDay(), context, nominator: 1, nominee: 2, raised: [1, 2]);
        var opened = DayPhaseFixture.Close(counted.State, context);
        Assert.Equal(StepMachineOutcomeKind.Applied, opened.Kind);
        Assert.Contains(opened.Events, item => item is ExtraNominationWindowOpenedEvent);
        return opened;
    }

    private static StepMachineOutcome NominateAndCount(
        StepMachineState state,
        SettlementContext context,
        int nominator,
        int nominee,
        params int[] raised)
    {
        var nominated = DayPhaseFixture.Nominate(state, context, nominator, nominee);
        Assert.True(nominated.Kind == StepMachineOutcomeKind.Applied, $"提名被拒：{nominated.RejectionCode}");
        var index = nominated.State.Day!.OpenDay!.OpenNomination!.Index;
        return DayPhaseFixture.SweepAndCount(nominated.State, context, index, raised);
    }

    private static SettlementContext Context(int seatCount) =>
        DayPhaseFixture.Context(
        [
            .. Enumerable.Range(1, seatCount).Select(value => (value, LifeState.Alive)),
        ]);

    private static SettlementContext WithSource(ExtraNominationOutcome outcome, int seat, int seatCount) =>
        Context(seatCount) with
        {
            ExtraNominations = [new FixedWindowSource(outcome, seat)],
        };

    /// <summary>脚本化窗口来源：只回答被要求的结论（本文件判内核，不判角色规则）。</summary>
    private sealed class FixedWindowSource(ExtraNominationOutcome outcome, int seat) : IExtraNominationSource
    {
        public CharacterId Character => new("test.window-source");

        public ExtraNominationAssessment? Evaluate(ExtraNominationContext context) => outcome switch
        {
            ExtraNominationOutcome.Available => new ExtraNominationAssessment
            {
                Outcome = outcome,
                Seat = new SeatId(seat),
                Note = "测试：有可用屠夫",
            },
            ExtraNominationOutcome.Indeterminate => new ExtraNominationAssessment
            {
                Outcome = outcome,
                Note = "测试：判定不了",
            },
            _ => new ExtraNominationAssessment
            {
                Outcome = outcome,
                Note = "测试：不可用",
            },
        };
    }
}
