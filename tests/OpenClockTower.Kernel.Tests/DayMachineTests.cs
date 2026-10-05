using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 白天规则（提名 / 收票后的计票 / 处决）的行为与边界；钟盘收票本身的语义见
/// <see cref="VoteSweepMachineTests"/>。
/// </summary>
/// <remarks>
/// 依据：百科《规则概要》三 /《提名》/《投票》/《处决》· 2026-10-01 抓取；
/// 钟盘收票口径见 <c>docs/standard/rulings.md</c> R-0017（目标形态）；自我提名见 R-0018（允许）。
/// </remarks>
public sealed class DayMachineTests
{
    [Fact]
    public void StartDay_EntersDayWindow_AndOpensDayLedger()
    {
        var state = DayPhaseFixture.StartDay();

        Assert.Equal(GamePhase.Day, state.Plan.Phase);
        Assert.Equal(StepSlotKind.DayWindow, state.CurrentSlot!.Kind);
        Assert.False(state.IsHeld);
        Assert.NotNull(state.Day);
        var day = Assert.IsType<DayRecord>(state.Day!.OpenDay);
        Assert.Equal(1, day.DayNumber);
        Assert.Equal(DayStatus.Open, day.Status);
    }

    [Fact]
    public void StartDay_RejectsWrongPlanShape()
    {
        var wrongPhase = DayPhaseFixture.Plan() with { Phase = GamePhase.OtherNight };
        Assert.Throws<ArgumentException>(() => StepMachine.StartDay(wrongPhase, 1));

        var noSlots = DayPhaseFixture.Plan() with { Slots = [] };
        Assert.Throws<ArgumentException>(() => StepMachine.StartDay(noSlots, 1));

        var twoSlots = DayPhaseFixture.Plan() with
        {
            Slots = [StepSlot.DayWindow(new StepSlotId("a")), StepSlot.DayWindow(new StepSlotId("b"))],
        };
        Assert.Throws<ArgumentException>(() => StepMachine.StartDay(twoSlots, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => StepMachine.StartDay(DayPhaseFixture.Plan(), 0));
    }

    [Fact]
    public void Nominate_OnlyAlivePlayersMayNominate()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Dead), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var rejected = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal("day.nominator_dead", DayPhaseFixture.CodeOf(rejected));
    }

    [Fact]
    public void Nominate_RejectsUnknownNominatorLife_AndUnknownSeats()
    {
        // 2 号在座次里、但没有生死事实：不猜。
        var context = DayPhaseFixture.Context((1, LifeState.Alive)) with
        {
            Seats = [new SeatId(1), new SeatId(2)],
        };
        var state = DayPhaseFixture.StartDay();

        var unknownLife = DayPhaseFixture.Nominate(state, context, nominator: 2, nominee: 1);
        Assert.Equal("day.nominator_life_unknown", DayPhaseFixture.CodeOf(unknownLife));

        // 9 号不在座次里。
        var unknownSeat = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 9);
        Assert.Equal("day.nominee_unknown", DayPhaseFixture.CodeOf(unknownSeat));
    }

    [Fact]
    public void Nominate_DeadPlayerCanBeNominated_AndSelfNominationIsAllowed()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Dead));
        var state = DayPhaseFixture.StartDay();

        var deadNominee = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, deadNominee.Kind);

        // 同一个白天：1 号已经发起过提名，换 3 号对 3 号自我提名（R-0018 允许）。
        var selfContext = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Dead), (3, LifeState.Alive));
        var counted = DayPhaseFixture.SweepAndCount(deadNominee.State, selfContext, index: 1);
        var selfNomination = DayPhaseFixture.Nominate(counted.State, selfContext, nominator: 3, nominee: 3);
        Assert.Equal(StepMachineOutcomeKind.Applied, selfNomination.Kind);
    }

    [Fact]
    public void Nominate_WhileAnotherNominationIsOpen_IsRejected()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive), (4, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var first = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, first.Kind);

        var second = DayPhaseFixture.Nominate(first.State, context, nominator: 3, nominee: 4);
        Assert.Equal("day.nomination_in_progress", DayPhaseFixture.CodeOf(second));
    }

    [Fact]
    public void Nominate_OncePerNominatorAndOncePerNomineePerDay()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var first = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(first.State, context, index: 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);

        var sameNominator = DayPhaseFixture.Nominate(counted.State, context, nominator: 1, nominee: 3);
        Assert.Equal("day.nominator_already_nominated", DayPhaseFixture.CodeOf(sameNominator));

        var sameNominee = DayPhaseFixture.Nominate(counted.State, context, nominator: 3, nominee: 2);
        Assert.Equal("day.nominee_already_nominated", DayPhaseFixture.CodeOf(sameNominee));
    }

    [Fact]
    public void CastVote_RequiresTheOpenNominationIndex()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var noOpen = DayPhaseFixture.Vote(state, context, voter: 1, index: 1, voted: true);
        Assert.Equal("day.no_open_nomination", DayPhaseFixture.CodeOf(noOpen));

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);

        // 新口径：还没点「开始收票」之前不能举手（R-0017 目标形态）。
        var beforeSweep = DayPhaseFixture.Vote(nominated.State, context, voter: 2, index: 1, voted: true);
        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(beforeSweep));

        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var wrongIndex = DayPhaseFixture.Vote(started.State, context, voter: 2, index: 9, voted: true);
        Assert.Equal("day.nomination_not_open", DayPhaseFixture.CodeOf(wrongIndex));

        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1);
        var afterCount = DayPhaseFixture.Vote(counted.State, context, voter: 2, index: 1, voted: true);
        Assert.Equal("day.no_open_nomination", DayPhaseFixture.CodeOf(afterCount));
    }

    [Fact]
    public void CountVotes_RequiresHalfOfAlivePlayers()
    {
        // 6 名存活：一半 = 3 票；2 票不算成功。
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive),
            (4, LifeState.Alive), (5, LifeState.Alive), (6, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 3, 4);

        var result = counted.Events.OfType<VoteCountedEvent>().Single();
        Assert.Equal(2, result.Voters.Count);
        Assert.Null(result.AboutToBeExecuted);
    }

    [Fact]
    public void CountVotes_ThreeConditions_MustAllHold()
    {
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive),
            (4, LifeState.Alive), (5, LifeState.Alive), (6, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        // 第一项：3 票（≥ 一半 3 票 + 至少 1 票 + 严格最多）→ 成立。
        var first = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var countedOne = DayPhaseFixture.SweepAndCount(first.State, context, index: 1, 3, 4, 5);
        Assert.Equal(new SeatId(2), countedOne.Events.OfType<VoteCountedEvent>().Single().AboutToBeExecuted);

        // 第二项：4 票超过 3 → 取代为 4 号（条件全满足）。
        var second = DayPhaseFixture.Nominate(countedOne.State, context, nominator: 3, nominee: 4);
        var countedTwo = DayPhaseFixture.SweepAndCount(second.State, context, index: 2, 1, 2, 5, 6);
        Assert.Equal(new SeatId(4), countedTwo.Events.OfType<VoteCountedEvent>().Single().AboutToBeExecuted);
    }

    [Fact]
    public void CountVotes_TieCancelsTheExistingCandidate()
    {
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive),
            (4, LifeState.Alive), (5, LifeState.Alive), (6, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var first = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var countedOne = DayPhaseFixture.SweepAndCount(first.State, context, index: 1, 3, 4, 5);
        Assert.Equal(new SeatId(2), countedOne.Events.OfType<VoteCountedEvent>().Single().AboutToBeExecuted);

        // 第二项同样 3 票：并列最多 → 两人都不再是「即将被处决」。
        var second = DayPhaseFixture.Nominate(countedOne.State, context, nominator: 2, nominee: 4);
        var countedTwo = DayPhaseFixture.SweepAndCount(second.State, context, index: 2, 5, 6, 1);
        Assert.Null(countedTwo.Events.OfType<VoteCountedEvent>().Single().AboutToBeExecuted);
    }

    [Fact]
    public void CastVote_DeadPlayerHasExactlyOneVoteAfterDeath()
    {
        // 3 号死亡：它可以投一次；2 名存活（1 / 2）时一票就过半数。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Dead));
        var state = DayPhaseFixture.StartDay();

        var first = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(first.State, context, index: 1, 3);
        var result = counted.Events.OfType<VoteCountedEvent>().Single();
        Assert.Contains(new SeatId(3), result.SpentVoteTokens);
        Assert.Contains(new SeatId(3), counted.State.Day!.SpentVoteTokens);

        // 第二次提名时 3 号再举手 → 票权已耗尽（先点「开始收票」才轮到票权校验）。
        var second = DayPhaseFixture.Nominate(counted.State, context, nominator: 2, nominee: 1);
        var started = DayPhaseFixture.StartSweep(second.State, context, 2);
        var again = DayPhaseFixture.Vote(started.State, context, voter: 3, index: 2, voted: true);
        Assert.Equal("day.vote_token_spent", DayPhaseFixture.CodeOf(again));
    }

    [Fact]
    public void CountVotes_RejectsWhenAnySeatLifeIsUnobserved()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);

        // 座次里多一个没有生死事实的 3 号：半数算不出来，拒绝整条计票。
        var incomplete = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive)) with
        {
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
        };
        var swept = DayPhaseFixture.RunSweep(nominated.State, context, index: 1);
        var counted = DayPhaseFixture.Count(swept, incomplete, index: 1);
        Assert.Equal("day.life_unobserved", DayPhaseFixture.CodeOf(counted));
    }

    [Fact]
    public void CloseDay_WithoutCandidate_ClosesWithoutExecution()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var closed = DayPhaseFixture.Close(state, context);

        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Empty(closed.Events.OfType<ExecutedEvent>());
        Assert.Single(closed.Events.OfType<DayClosedEvent>());
        Assert.True(closed.State.IsPlanCompleted);
        Assert.Null(closed.State.Day!.OpenDay);
        Assert.Null(closed.State.Day.Days[^1].Executed);

        // 关账之后「昨天」才是那个白天：夜晚能力读的就是这一份（R-0058）。
        Assert.Equal(closed.State.Day.Days[^1], closed.State.Day.LastClosedDay);
        Assert.Null(state.Day!.LastClosedDay);
    }

    [Fact]
    public void CloseDay_ExecutesCandidate_AndDeathIsASeparateFact()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 3, 1);

        var closed = DayPhaseFixture.Close(counted.State, context);

        var executed = closed.Events.OfType<ExecutedEvent>().Single();
        Assert.Equal(new SeatId(2), executed.Seat);
        var death = closed.Events.OfType<SeatStateChangedEvent>().Single();
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Equal(DayMachine.ExecutionDeathReason, death.Reason);
        Assert.Equal(new SeatId(2), closed.State.Day!.Days[^1].Executed);

        // 账里也折出死亡：处决与死亡是两条事实、同一批提交。
        var ledger = GameStateMachine.Fold(closed.Events);
        Assert.Equal(LifeState.Dead, ledger.Seat(new SeatId(2))!.LifeValue);
    }

    [Fact]
    public void CloseDay_RejectsWhenANominationIsStillOpen()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var closed = DayPhaseFixture.Close(nominated.State, context);
        Assert.Equal("day.nomination_not_counted", DayPhaseFixture.CodeOf(closed));
    }

    [Fact]
    public void CloseDay_AlreadyDeadCandidate_IsExecutedWithoutADeathChange()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 3, 1);

        // 计票之后、结束之前 2 号死亡（例如其它效果）：仍然"被处决"，但不再产生死亡变化。
        var died = DayPhaseFixture.Apply(counted.State, context, new SeatStateChangedInput
        {
            Seat = new SeatId(2),
            Life = LifeState.Dead,
            Reason = "test.death",
        });

        // 内核读的是"当下的账"：这里显式给一份 2 号已死亡的上下文（应用层由事件折叠保证同源）。
        var diedContext = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Dead), (3, LifeState.Alive));
        var closed = DayPhaseFixture.Close(died.State, diedContext);

        Assert.Single(closed.Events.OfType<ExecutedEvent>());
        Assert.Empty(closed.Events.OfType<SeatStateChangedEvent>());
    }

    [Fact]
    public void QuotaElapsed_OnDayWindow_IsANoOp()
    {
        var state = DayPhaseFixture.StartDay();
        var outcome = DayPhaseFixture.Apply(state, SettlementContext.Empty, new SlotQuotaElapsedInput());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Empty(outcome.Events);
        Assert.Equal(state, outcome.State);
    }

    [Fact]
    public void ForceAdvance_EndsDayWithoutExecution()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 3);

        var forced = DayPhaseFixture.Apply(counted.State, context, new ForceAdvanceInput { Reason = "测试强推" });
        Assert.Equal(StepMachineOutcomeKind.Applied, forced.Kind);
        Assert.Empty(forced.Events.OfType<ExecutedEvent>());
        Assert.Single(forced.Events.OfType<DayClosedEvent>());
        Assert.True(forced.State.IsPlanCompleted);
    }

    [Fact]
    public void StartPhase_AfterDay_KeepsTheDayLedger()
    {
        // 回归：开新阶段曾经从零折叠，丢掉白天账（票权 / 逐日事实）——新阶段必须接在现有状态上。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Dead));
        var state = DayPhaseFixture.StartDay();
        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 3);
        var closed = DayPhaseFixture.Close(counted.State, context);
        Assert.Contains(new SeatId(3), closed.State.Day!.SpentVoteTokens);

        var night = StepFixture.Plan("sv:night-2", StepFixture.Empty("slot-1"));
        var started = StepMachine.StartPhase(night, closed.State);

        Assert.NotNull(started.State.Day);
        Assert.Single(started.State.Day!.Days);
        Assert.Contains(new SeatId(3), started.State.Day.SpentVoteTokens);
        Assert.Equal("sv:night-2", started.State.Plan.Label);
    }

    [Fact]
    public void ForceAdvance_WithAnOpenNomination_IsRejected()
    {
        // 对抗性复核 F-2：强推不能替说书人拍板计票结论，否则白天账里会留下永远停在 Voting 的提名。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();
        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);

        var forced = DayPhaseFixture.Apply(nominated.State, context, new ForceAdvanceInput { Reason = "测试强推" });
        Assert.Equal("day.nomination_not_counted", DayPhaseFixture.CodeOf(forced));

        // 先收票再计票，然后强推：出路永远存在（且票权在计票时结算）。
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1);
        var afterCount = DayPhaseFixture.Apply(counted.State, context, new ForceAdvanceInput { Reason = "测试强推" });
        Assert.Equal(StepMachineOutcomeKind.Applied, afterCount.Kind);
        Assert.True(afterCount.State.IsPlanCompleted);
        Assert.DoesNotContain(
            afterCount.State.Day!.Days[^1].Nominations,
            nomination => nomination.Status == NominationStatus.Voting);
    }

    [Fact]
    public void CastVote_DeadPlayerVoteOnAFailedCount_StillSpendsTheToken()
    {
        // "只要他举手且票数被成功统计，他就会失去投票机会"——投票本身失败也一样消耗（百科《投票》）。
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive),
            (4, LifeState.Alive), (5, LifeState.Alive), (6, LifeState.Dead));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var counted = DayPhaseFixture.SweepAndCount(nominated.State, context, index: 1, 6);

        var result = counted.Events.OfType<VoteCountedEvent>().Single();
        Assert.Null(result.AboutToBeExecuted); // 1 票达不到 5 名存活者的一半 → 本次投票失败
        Assert.Contains(new SeatId(6), result.SpentVoteTokens);
        Assert.Contains(new SeatId(6), counted.State.Day!.SpentVoteTokens);

        // 下一次提名时 6 号已无票权。
        var second = DayPhaseFixture.Nominate(counted.State, context, nominator: 2, nominee: 3);
        var started = DayPhaseFixture.StartSweep(second.State, context, 2);
        var again = DayPhaseFixture.Vote(started.State, context, voter: 6, index: 2, voted: true);
        Assert.Equal("day.vote_token_spent", DayPhaseFixture.CodeOf(again));
    }

    [Fact]
    public void Replay_RejectsACountListThatDoesNotMatchTheFoldedBallot()
    {
        // 折叠层不许"以计票事件里的名单为准"改写票面：名单与已折叠票面不一致 = 事件流被改写，必须显式失败。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var started = StepMachine.StartDay(DayPhaseFixture.Plan(), 1);
        var nominated = DayPhaseFixture.Nominate(started.State, context, nominator: 1, nominee: 2);
        var sweep = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var voted = DayPhaseFixture.Vote(sweep.State, context, voter: 3, index: 1, voted: true);
        var collected = DayPhaseFixture.CollectAll(voted.State, context, 1);

        var events = new List<GameEvent>(started.Events);
        events.AddRange(nominated.Events);
        events.AddRange(sweep.Events);
        events.AddRange(voted.Events);
        events.AddRange(collected.Events);
        events.Add(new VoteCountedEvent
        {
            DayNumber = 1,
            NominationIndex = 1,
            Voters = [new SeatId(1), new SeatId(3)],
            SpentVoteTokens = [],
            AboutToBeExecuted = null,
        });

        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(events));
    }

    [Fact]
    public void Replay_FoldingTheProducedEvents_EqualsTheHandledState()
    {
        var context = DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive), (4, LifeState.Dead));
        var started = StepMachine.StartDay(DayPhaseFixture.Plan(), 1);
        var state = started.State;

        var events = new List<GameEvent>(started.Events);
        void Step(StepMachineOutcome outcome)
        {
            Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
            events.AddRange(outcome.Events);
            state = outcome.State;
        }

        Step(DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2));
        Step(DayPhaseFixture.StartSweep(state, context, 1));
        Step(DayPhaseFixture.Vote(state, context, voter: 3, index: 1, voted: true));
        Step(DayPhaseFixture.Vote(state, context, voter: 4, index: 1, voted: true));
        Step(DayPhaseFixture.CollectAll(state, context, 1));
        Step(DayPhaseFixture.Count(state, context, index: 1));
        Step(DayPhaseFixture.Nominate(state, context, nominator: 2, nominee: 4));
        Step(DayPhaseFixture.StartSweep(state, context, 2));
        Step(DayPhaseFixture.Vote(state, context, voter: 1, index: 2, voted: true));
        Step(DayPhaseFixture.CollectAll(state, context, 2));
        Step(DayPhaseFixture.Count(state, context, index: 2));
        Step(DayPhaseFixture.Close(state, context));

        var folded = StepMachine.Fold(events);
        Assert.NotNull(folded);
        Assert.True(
            StepMachineStateComparer.AreEquivalent(state, folded),
            "重放折叠出的白天账必须与逐条处理后的状态等价（含收票进度、票权与处决）。");
    }
}
