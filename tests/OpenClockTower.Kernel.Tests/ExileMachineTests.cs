using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 流放流程的内核用例（票据 `traveller-and-exile` · D2）：提议资格、钟盘串行、分母时点、
/// 死者票权、阈值边界、死亡收口与折叠损坏的显式失败。
/// </summary>
/// <remarks>
/// 规则依据：<c>docs/standard/rulings.md</c> R-0044（三分流 / 阈值 / 公开面 / 能力边界）与 R-0045
/// （死亡后果面）；平台实施口径（钟盘串行、分母快照、拒绝码）见票据「D2 实施口径」。
/// </remarks>
public sealed class ExileMachineTests
{
    [Fact]
    public void Propose_RequiresOpenDayAndInGameSeats()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive));

        // 白天没开（已关闭）：任何提议都被拒。
        Assert.Equal(
            "day.not_open",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(ExilePhaseFixture.ClosedDay(), context, 1, 2)));

        var state = ExilePhaseFixture.StartDay();
        Assert.Equal(
            "day.exile_proposer_not_in_game",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(state, context, proposer: 9, target: 2)));
        Assert.Equal(
            "day.exile_target_not_in_game",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(state, context, proposer: 1, target: 9)));
    }

    [Fact]
    public void Propose_RequiresObservedTravellerCharacterAndFacts()
    {
        var unobserved = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, null, LifeState.Alive));
        Assert.Equal(
            "day.exile_target_character_unknown",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), unobserved, 1, 2)));

        var noFacts = ExilePhaseFixture.ContextWithoutCharacterFacts(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive));
        Assert.Equal(
            "day.exile_character_facts_missing",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), noFacts, 1, 2)));

        var nonTraveller = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "artist", LifeState.Alive));
        Assert.Equal(
            "day.exile_target_not_traveller",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), nonTraveller, 1, 2)));
    }

    [Fact]
    public void Propose_AllowsDeadProposerAndDeadTravellerTarget()
    {
        // 发起人含死者（R-0044 第 2 条）：生死不是发起资格的一部分。
        var deadProposer = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Dead),
            (2, "barista", LifeState.Alive));
        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), deadProposer, 1, 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, proposed.Kind);
        var proposedEvent = Assert.Single(proposed.Events.OfType<ExileProposedEvent>());
        Assert.Equal(1, proposedEvent.ExileIndex);
        Assert.Equal(new SeatId(1), proposedEvent.Proposer);
        Assert.Equal(new SeatId(2), proposedEvent.Target);

        // 目标只要求"在局旅行者"：已死的旅行者也能被提议（死亡收口时不再重复记死亡）。
        var deadTarget = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Dead));
        Assert.Equal(
            StepMachineOutcomeKind.Applied,
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), deadTarget, 1, 2).Kind);
    }

    [Fact]
    public void Propose_AllowsOnlyOneOpenExile_AndOneProposalPerTravellerPerDay()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "deviant", LifeState.Alive));

        var first = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, first.Kind);

        // 同日多条流放顺序进行：上一条没结清之前，不能登记下一条。
        Assert.Equal(
            "day.exile_in_progress",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(first.State, context, proposer: 1, target: 3)));

        // 结清第一条（1 票不足 3 席的一半）后：同一名旅行者当天不能再被提议，另一名可以。
        var settled = ExilePhaseFixture.SweepAndCount(first.State, context, 1, 1);
        Assert.Equal(
            ExileConclusion.VotesInsufficient,
            settled.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        Assert.Equal(
            "day.exile_already_proposed",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Propose(settled.State, context, proposer: 1, target: 2)));
        Assert.Equal(
            StepMachineOutcomeKind.Applied,
            ExilePhaseFixture.Propose(settled.State, context, proposer: 1, target: 3).Kind);
    }

    [Fact]
    public void StartSweep_ValidatesOpenExileIndexParametersAndSeats()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive));
        var state = ExilePhaseFixture.StartDay();

        Assert.Equal("day.no_open_exile", DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(state, context, 1)));

        var proposed = ExilePhaseFixture.Propose(state, context, 1, 2);
        Assert.Equal("day.exile_not_open", DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(proposed.State, context, 2)));
        Assert.Equal(
            "day.sweep_countdown_invalid",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(proposed.State, context, 1, countdownMilliseconds: 999)));
        Assert.Equal(
            "day.sweep_interval_invalid",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(proposed.State, context, 1, intervalMilliseconds: 299)));

        var noSeats = context with { Seats = [] };
        Assert.Equal("day.no_seats", DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(proposed.State, noSeats, 1)));

        var started = ExilePhaseFixture.StartSweep(proposed.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, started.Kind);
        var startedEvent = started.Events.OfType<ExileSweepStartedEvent>().Single();
        Assert.Equal(new[] { new SeatId(1), new SeatId(2) }, startedEvent.Seats);
        Assert.Equal(
            "day.sweep_started",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(started.State, context, 1)));
    }

    [Fact]
    public void Dial_SerializesNominationsAndExiles_BothDirections()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "artist", LifeState.Alive),
            (3, "barista", LifeState.Alive));

        // 提名收票没走完 → 流放不能开；提案本身不受钟盘占用限制（可随时登记）。
        var nominated = DayPhaseFixture.Nominate(ExilePhaseFixture.StartDay(), context, nominator: 1, nominee: 2);
        var nominationSweep = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var proposed = ExilePhaseFixture.Propose(nominationSweep.State, context, proposer: 1, target: 3);
        Assert.Equal(StepMachineOutcomeKind.Applied, proposed.Kind);
        Assert.Equal(
            "day.ballot_in_progress",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.StartSweep(proposed.State, context, 1)));

        // 提名收票收完（还没计票）→ 不占钟盘：流放可以开始，提名可以稍后再计票。
        var nominationCollected = DayPhaseFixture.CollectAll(proposed.State, context, 1);
        var exileStarted = ExilePhaseFixture.StartSweep(nominationCollected.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, exileStarted.Kind);
        var countedNomination = DayPhaseFixture.Count(nominationCollected.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, countedNomination.Kind);

        // 反向：流放收票没走完时，提名收票不能开（但提名本身可以照常发起）。
        var secondContext = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "artist", LifeState.Alive),
            (3, "barista", LifeState.Alive));
        var secondProposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), secondContext, 1, 3);
        var exileFirst = ExilePhaseFixture.StartSweep(secondProposed.State, secondContext, 1);
        var nominatedLate = DayPhaseFixture.Nominate(exileFirst.State, secondContext, nominator: 1, nominee: 2);
        Assert.Equal(StepMachineOutcomeKind.Applied, nominatedLate.Kind);
        Assert.Equal(
            "day.ballot_in_progress",
            DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(nominatedLate.State, secondContext, 1)));

        // 流放结清后，提名继续（"提名可在流放结清后继续"，票据 D2 实施口径）。
        var raisedA = ExilePhaseFixture.Vote(nominatedLate.State, secondContext, voter: 1, index: 1, voted: true);
        var raisedB = ExilePhaseFixture.Vote(raisedA.State, secondContext, voter: 2, index: 1, voted: true);
        var exileCollected = ExilePhaseFixture.CollectAll(raisedB.State, secondContext, 1);
        var exileSettled = ExilePhaseFixture.Count(exileCollected.State, secondContext, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, exileSettled.Kind);
        Assert.Equal(
            StepMachineOutcomeKind.Applied,
            DayPhaseFixture.StartSweep(exileSettled.State, secondContext, 1).Kind);
    }

    [Fact]
    public void Collect_EnforcesOrder_AndFreezesHandsAtTheMoment()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var started = ExilePhaseFixture.StartSweep(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2).State,
            context,
            1);

        Assert.Equal(
            "day.seat_out_of_order",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Collect(started.State, context, 1, seat: 2)));

        // 1 号先举手再放下（收票前）：收票那一刻手是放下的 → 不计票（先举也算、过时不候的反面）。
        var raised = ExilePhaseFixture.Vote(started.State, context, voter: 1, index: 1, voted: true);
        var lowered = ExilePhaseFixture.Vote(raised.State, context, voter: 1, index: 1, voted: false);
        var first = ExilePhaseFixture.Collect(lowered.State, context, 1, seat: 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, first.Kind);
        Assert.False(first.Events.OfType<ExileSeatVoteCollectedEvent>().Single().Voted);
        Assert.Equal(
            "day.seat_collected",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Vote(first.State, context, voter: 1, index: 1, voted: true)));

        // 先举也算：3 号在倒计时里举手，收票时被冻结为赞成。
        var voted = ExilePhaseFixture.Vote(first.State, context, voter: 3, index: 1, voted: true);
        var complete = ExilePhaseFixture.CollectAll(voted.State, context, 1);
        var exile = complete.State.Day!.OpenDay!.OpenExile!;
        Assert.Equal(new[] { new SeatId(3) }, exile.Ballot);
        Assert.Equal(new[] { new SeatId(3) }, exile.HandsRaised);
        Assert.True(exile.Sweep!.IsComplete);
    }

    [Fact]
    public void CastVote_AllowsDeadVoterWithSpentToken_AndNeverConsumesTokens()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Dead));

        // 3 号的「死后仅一次」提名投票权已经用掉：流放表决**不查也不耗**（R-0044 第 1 / 4 条）。
        var state = ExilePhaseFixture.StartDay() with
        {
            Day = new DayState
            {
                Days = [new DayRecord { DayNumber = 1, Status = DayStatus.Open }],
                SpentVoteTokens = [new SeatId(3)],
            },
        };

        var proposed = ExilePhaseFixture.Propose(state, context, 1, 2);
        var counted = ExilePhaseFixture.SweepAndCount(proposed.State, context, 1, 1, 3);
        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        Assert.Equal(
            new[] { new SeatId(1), new SeatId(3) },
            counted.Events.OfType<ExileVoteCountedEvent>().Single().Voters);
        Assert.Equal(new[] { new SeatId(3) }, counted.State.Day!.SpentVoteTokens);
    }

    [Fact]
    public void CastVote_RejectsVoterOutsideTheBallotSnapshot_OrAlreadyLeft()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var started = ExilePhaseFixture.StartSweep(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2).State,
            context,
            1);

        // 4 号不在本局：收票名册（快照）里没有他。
        Assert.Equal(
            "day.exile_voter_not_on_ballot",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Vote(started.State, context, voter: 4, index: 1, voted: true)));

        // 2 号在快照里，但已经离场：不再参与表决（R-0044 第 6 条）。
        var departed = context with { Seats = [new SeatId(1), new SeatId(3)] };
        Assert.Equal(
            "day.exile_voter_not_in_game",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Vote(started.State, departed, voter: 2, index: 1, voted: true)));
    }

    [Fact]
    public void Count_RequiresCompleteSweep_AndRejectsWhenTargetLeftMidVote()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2);

        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(ExilePhaseFixture.Count(proposed.State, context, 1)));
        var started = ExilePhaseFixture.StartSweep(proposed.State, context, 1);
        Assert.Equal("day.sweep_incomplete", DayPhaseFixture.CodeOf(ExilePhaseFixture.Count(started.State, context, 1)));

        // 目标在收票途中离场：达线也不产生死亡——显式拒绝，不静默了结（防御性；正常入口由离场闸拦住）。
        var complete = ExilePhaseFixture.RunSweep(proposed.State, context, 1, 1, 3);
        var targetLeft = context with { Seats = [new SeatId(1), new SeatId(3)] };
        Assert.Equal("day.exile_target_left", DayPhaseFixture.CodeOf(ExilePhaseFixture.Count(complete, targetLeft, 1)));
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(3, 1, false)]
    [InlineData(3, 2, true)]
    [InlineData(4, 1, false)]
    [InlineData(4, 2, true)]
    [InlineData(5, 2, false)]
    [InlineData(5, 3, true)]
    public void Count_ThresholdIsHalfOfTheSnapshotRoundedUp(int seatCount, int voteCount, bool exiled)
    {
        var seats = new List<(int Seat, string? Character, LifeState Life)>();
        for (var seat = 1; seat <= seatCount; seat++)
        {
            seats.Add((seat, seat == seatCount ? "barista" : "clockmaker", LifeState.Alive));
        }

        var context = ExilePhaseFixture.Context(seats.ToArray());
        var counted = ExilePhaseFixture.SweepAndCount(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: seatCount).State,
            context,
            index: 1,
            raised: [.. Enumerable.Range(1, voteCount)]);

        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        Assert.Equal(
            exiled ? ExileConclusion.Exiled : ExileConclusion.VotesInsufficient,
            counted.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        Assert.Equal(
            exiled,
            counted.Events.OfType<SeatStateChangedEvent>().Any(changed => changed.Seat == new SeatId(seatCount)));
    }

    [Fact]
    public void Count_ExiledKillsAliveTarget_AndFoldsTheConclusion()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var counted = ExilePhaseFixture.SweepAndCount(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2).State,
            context,
            index: 1,
            raised: [1, 3]);

        Assert.Equal(
            ExileConclusion.Exiled,
            counted.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        var death = Assert.Single(counted.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Equal(ExileMachine.ExileDeathReason, death.Reason);

        var exile = counted.State.Day!.OpenDay!.Exiles.Single();
        Assert.Equal(ExileStatus.Counted, exile.Status);
        Assert.Equal(ExileConclusion.Exiled, exile.Conclusion);
        Assert.Equal(new[] { new SeatId(1), new SeatId(3) }, exile.Ballot);
    }

    [Fact]
    public void Count_DeadTarget_RecordsConclusionWithoutSecondDeath()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Dead),
            (3, "artist", LifeState.Alive));
        var counted = ExilePhaseFixture.SweepAndCount(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2).State,
            context,
            index: 1,
            raised: [1, 3]);

        Assert.Equal(
            ExileConclusion.Exiled,
            counted.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);
        Assert.Empty(counted.Events.OfType<SeatStateChangedEvent>());
    }

    [Fact]
    public void Count_RejectsWhenTargetLifeIsUnobserved()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("clockmaker"),
                Life = LifeState.Alive,
                Reason = "test.exile.setup",
            },
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("barista"),
                Reason = "test.exile.setup",
            },
            new SeatStateChangedEvent
            {
                Seat = new SeatId(3),
                Character = new CharacterId("artist"),
                Life = LifeState.Alive,
                Reason = "test.exile.setup",
            },
        ]);
        var context = ExilePhaseFixture.ContextOf(state, 1, 2, 3);

        // 目标生死没观测：达线也不猜"死没死"，整条计票显式拒绝（D-0015）。
        var complete = ExilePhaseFixture.RunSweep(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2).State,
            context,
            index: 1,
            raised: [1, 3]);
        Assert.Equal(
            "day.exile_target_life_unknown",
            DayPhaseFixture.CodeOf(ExilePhaseFixture.Count(complete, context, 1)));
    }

    [Fact]
    public void Resume_ContinuesFromNextSeat_AndRejectsWhenComplete()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2);

        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(ExilePhaseFixture.ResumeSweep(proposed.State, context, 1)));

        var started = ExilePhaseFixture.StartSweep(proposed.State, context, 1);
        var first = ExilePhaseFixture.Collect(started.State, context, 1, seat: 1);
        var resumed = ExilePhaseFixture.ResumeSweep(first.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, resumed.Kind);
        Assert.IsType<ExileSweepResumedEvent>(Assert.Single(resumed.Events));

        var complete = ExilePhaseFixture.CollectAll(resumed.State, context, 1);
        Assert.Equal("day.sweep_complete", DayPhaseFixture.CodeOf(ExilePhaseFixture.ResumeSweep(complete.State, context, 1)));
    }

    [Fact]
    public void DayCannotCloseOrForceAdvance_WhileAnExileIsUnsettled()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive));
        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2);

        Assert.Equal("day.exile_not_counted", DayPhaseFixture.CodeOf(DayPhaseFixture.Close(proposed.State, context)));
        var forced = StepMachine.Handle(proposed.State, new ForceAdvanceInput { Reason = "测试强推" });
        Assert.Equal(StepMachineOutcomeKind.Rejected, forced.Kind);
        Assert.Equal("day.exile_not_counted", forced.RejectionCode);

        // 结清之后白天可以正常关闭。
        var settled = ExilePhaseFixture.SweepAndCount(proposed.State, context, 1, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, DayPhaseFixture.Close(settled.State, context).Kind);
    }

    [Fact]
    public void MultipleExiles_SameDay_RunSequentially()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "deviant", LifeState.Alive));

        // 第 1 条（对 2 号，1 票不足 3 席的一半）→ 存活、额度耗尽。
        var first = ExilePhaseFixture.SweepAndCount(
            ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, proposer: 1, target: 2).State,
            context,
            1,
            1);
        Assert.Equal(
            ExileConclusion.VotesInsufficient,
            first.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);

        // 第 2 条（对 3 号，2 票达线）→ 死亡；两条按顺序同账。
        var second = ExilePhaseFixture.SweepAndCount(
            ExilePhaseFixture.Propose(first.State, context, proposer: 2, target: 3).State,
            context,
            2,
            1,
            2);
        Assert.Equal(
            ExileConclusion.Exiled,
            second.Events.OfType<ExileVoteCountedEvent>().Single().Conclusion);

        var exiles = second.State.Day!.OpenDay!.Exiles;
        Assert.Equal(2, exiles.Count);
        Assert.Equal(1, exiles[0].Index);
        Assert.Equal(new SeatId(2), exiles[0].Target);
        Assert.Equal(ExileConclusion.VotesInsufficient, exiles[0].Conclusion);
        Assert.Equal(2, exiles[1].Index);
        Assert.Equal(new SeatId(3), exiles[1].Target);
        Assert.Equal(ExileConclusion.Exiled, exiles[1].Conclusion);
    }

}
