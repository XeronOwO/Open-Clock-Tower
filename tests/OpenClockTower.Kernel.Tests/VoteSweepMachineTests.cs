using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 钟盘收票（R-0017 目标形态）：开始 / 逐席冻结 / 继续 / 计票前置 / 参数无关性 / 回放完整性。
/// </summary>
/// <remarks>
/// 严格时点语义：先举也算、过时不候（分针指向该席的那一刻读已登记的举手状态）；
/// 节奏参数只进事件流、不参与判定；收票没走完不能计票，事件流里出现这种组合必须显式失败。
/// </remarks>
public sealed class VoteSweepMachineTests
{
    [Fact]
    public void StartVoteSweep_RequiresOpenNomination_AndValidParameters()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        Assert.Equal("day.no_open_nomination", DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(state, context, 1)));

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal("day.nomination_not_open", DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(nominated.State, context, index: 2)));
        Assert.Equal(
            "day.sweep_countdown_invalid",
            DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(nominated.State, context, 1, countdownMilliseconds: 999)));
        Assert.Equal(
            "day.sweep_interval_invalid",
            DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(nominated.State, context, 1, intervalMilliseconds: 299)));

        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, started.Kind);
        var startedEvent = Assert.Single(started.Events.OfType<VoteSweepStartedEvent>());
        Assert.Equal(new[] { new SeatId(1), new SeatId(2) }, startedEvent.Seats);
        Assert.Equal(VoteSweepLimits.DefaultCountdownMilliseconds, startedEvent.CountdownMilliseconds);
        Assert.Equal(VoteSweepLimits.DefaultIntervalMilliseconds, startedEvent.IntervalMilliseconds);

        Assert.Equal("day.sweep_started", DayPhaseFixture.CodeOf(DayPhaseFixture.StartSweep(started.State, context, 1)));
    }

    [Fact]
    public void CastVote_RaiseAndLowerBeforeCollection_OnlyTheFrozenMomentCounts()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var voted = DayPhaseFixture.Vote(started.State, context, voter: 3, index: 1, voted: true);
        var withdrawn = DayPhaseFixture.Vote(voted.State, context, voter: 3, index: 1, voted: false);

        // 3 号在收票前放手：收票那一刻手是放下的 → 不计票（先举也算、过时不候的反面）。
        var collected = DayPhaseFixture.CollectAll(withdrawn.State, context, 1);
        var counted = DayPhaseFixture.Count(collected.State, context, index: 1);
        var voteCounted = counted.Events.OfType<VoteCountedEvent>().Single();
        Assert.Empty(voteCounted.Voters);
        Assert.Null(voteCounted.AboutToBeExecuted);

        // 对照：举手不放下的席位会被冻结为赞成。
        var second = DayPhaseFixture.Nominate(counted.State, context, nominator: 2, nominee: 1);
        var secondCounted = DayPhaseFixture.SweepAndCount(second.State, context, index: 2, 3);
        Assert.Contains(new SeatId(3), secondCounted.Events.OfType<VoteCountedEvent>().Single().Voters);
    }

    [Fact]
    public void CastVote_AfterSeatCollected_IsRejected_ByTimeSemantics()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var first = DayPhaseFixture.Collect(started.State, context, 1, seat: 1);

        // 分针已经过了 1 号：过时不候，不能再举手 / 放下。
        var late = DayPhaseFixture.Vote(first.State, context, voter: 1, index: 1, voted: true);
        Assert.Equal("day.seat_collected", DayPhaseFixture.CodeOf(late));

        var complete = DayPhaseFixture.CollectAll(first.State, context, 1);
        var counted = DayPhaseFixture.Count(complete.State, context, index: 1);
        Assert.DoesNotContain(new SeatId(1), counted.Events.OfType<VoteCountedEvent>().Single().Voters);
    }

    [Fact]
    public void CollectSeatVote_MustFollowOrder_AndRequiresStartedSweep()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(DayPhaseFixture.Collect(nominated.State, context, 1, seat: 1)));

        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        Assert.Equal("day.seat_out_of_order", DayPhaseFixture.CodeOf(DayPhaseFixture.Collect(started.State, context, 1, seat: 2)));

        var first = DayPhaseFixture.Collect(started.State, context, 1, seat: 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, first.Kind);
        Assert.Equal(new SeatId(1), Assert.Single(first.Events.OfType<SeatVoteCollectedEvent>()).Seat);
        Assert.Equal("day.seat_out_of_order", DayPhaseFixture.CodeOf(DayPhaseFixture.Collect(first.State, context, 1, seat: 1)));

        var complete = DayPhaseFixture.CollectAll(first.State, context, 1);
        Assert.Equal("day.sweep_complete", DayPhaseFixture.CodeOf(DayPhaseFixture.Collect(complete.State, context, 1, seat: 3)));
    }

    [Fact]
    public void CountVotes_RejectsBeforeSweepStartsOrCompletes()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();

        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);
        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(DayPhaseFixture.Count(nominated.State, context, 1)));

        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        Assert.Equal("day.sweep_incomplete", DayPhaseFixture.CodeOf(DayPhaseFixture.Count(started.State, context, 1)));

        var partial = DayPhaseFixture.Collect(started.State, context, 1, seat: 1);
        Assert.Equal("day.sweep_incomplete", DayPhaseFixture.CodeOf(DayPhaseFixture.Count(partial.State, context, 1)));

        var complete = DayPhaseFixture.CollectAll(partial.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, DayPhaseFixture.Count(complete.State, context, 1).Kind);
    }

    [Fact]
    public void VoteSweep_ParametersDoNotChangeAdjudication()
    {
        static SettlementContext Context6() => DayPhaseFixture.Context(
            (1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive),
            (4, LifeState.Alive), (5, LifeState.Alive), (6, LifeState.Alive));

        // 默认参数：3s / 1s。
        var defaultContext = Context6();
        var defaultNominated = DayPhaseFixture.Nominate(DayPhaseFixture.StartDay(), defaultContext, 1, 2);
        var defaultCounted = DayPhaseFixture.SweepAndCount(defaultNominated.State, defaultContext, 1, 3, 4, 5);

        // 自定义参数：1.5s / 0.3s。
        var customContext = Context6();
        var customNominated = DayPhaseFixture.Nominate(DayPhaseFixture.StartDay(), customContext, 1, 2);
        var started = DayPhaseFixture.StartSweep(
            customNominated.State,
            customContext,
            1,
            countdownMilliseconds: 1500,
            intervalMilliseconds: 300);
        var after = started.State;
        foreach (var seat in new[] { 3, 4, 5 })
        {
            after = DayPhaseFixture.Vote(after, customContext, seat, 1, voted: true).State;
        }

        var customCounted = DayPhaseFixture.Count(
            DayPhaseFixture.CollectAll(after, customContext, 1).State,
            customContext,
            1);

        var first = defaultCounted.Events.OfType<VoteCountedEvent>().Single();
        var second = customCounted.Events.OfType<VoteCountedEvent>().Single();
        Assert.Equal(first.Voters, second.Voters);
        Assert.Equal(first.AboutToBeExecuted, second.AboutToBeExecuted);
        Assert.Equal(first.SpentVoteTokens, second.SpentVoteTokens);

        // 呈现参数只记录在事件里，不参与判定。
        var startedEvent = Assert.Single(started.Events.OfType<VoteSweepStartedEvent>());
        Assert.Equal(1500, startedEvent.CountdownMilliseconds);
        Assert.Equal(300, startedEvent.IntervalMilliseconds);
    }

    [Fact]
    public void VoteSweep_Resume_ContinuesFromNextSeat_AndRejectsWhenComplete()
    {
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var state = DayPhaseFixture.StartDay();
        var nominated = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);

        Assert.Equal("day.sweep_not_started", DayPhaseFixture.CodeOf(DayPhaseFixture.ResumeSweep(nominated.State, context, 1)));

        var started = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var first = DayPhaseFixture.Collect(started.State, context, 1, seat: 1);
        var resumed = DayPhaseFixture.ResumeSweep(first.State, context, 1);
        Assert.Equal(StepMachineOutcomeKind.Applied, resumed.Kind);
        Assert.IsType<VoteSweepResumedEvent>(Assert.Single(resumed.Events));

        var complete = DayPhaseFixture.CollectAll(resumed.State, context, 1);
        Assert.Equal("day.sweep_complete", DayPhaseFixture.CodeOf(DayPhaseFixture.ResumeSweep(complete.State, context, 1)));
    }

    [Fact]
    public void Replay_LegacyWindowStream_StillFolds_AndMigratesToSweep()
    {
        // 旧日志兼容（R-0017「历史口径」）：没有 VoteSweepStartedEvent 的事件流仍按旧形态折叠
        // （VoteCastEvent 即时改票面），旧的计票事件只要与票面一致就能回放。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var started = StepMachine.StartDay(DayPhaseFixture.Plan(), 1);
        var nominated = DayPhaseFixture.Nominate(started.State, context, nominator: 1, nominee: 2);
        var legacyVote = new VoteCastEvent
        {
            DayNumber = 1,
            NominationIndex = 1,
            Voter = new SeatId(3),
            Voted = true,
        };

        var legacyCounted = StepMachine.Fold(
        [
            .. started.Events,
            .. nominated.Events,
            legacyVote,
            new VoteCountedEvent
            {
                DayNumber = 1,
                NominationIndex = 1,
                Voters = [new SeatId(3)],
                SpentVoteTokens = [],
                AboutToBeExecuted = new SeatId(2),
            },
        ]);

        Assert.NotNull(legacyCounted);
        var countedNomination = legacyCounted!.Day!.Days[^1].Nominations[0];
        Assert.Equal(NominationStatus.Counted, countedNomination.Status);
        Assert.Equal(new[] { new SeatId(3) }, countedNomination.Ballot);
        Assert.Null(countedNomination.Sweep);

        // 迁移：旧流里还没计票的提名，开始收票时旧窗口票面不并入冻结结论（玩家在倒计时里重新举手）。
        var legacyOpen = StepMachine.Fold([.. started.Events, .. nominated.Events, legacyVote]);
        Assert.NotNull(legacyOpen);
        var sweep = StepMachine.Handle(legacyOpen!, context, new StartVoteSweepInput
        {
            NominationIndex = 1,
            CountdownMilliseconds = VoteSweepLimits.DefaultCountdownMilliseconds,
            IntervalMilliseconds = VoteSweepLimits.DefaultIntervalMilliseconds,
        });
        Assert.Equal(StepMachineOutcomeKind.Applied, sweep.Kind);
        var swept = sweep.State.Day!.OpenDay!.OpenNomination!;
        Assert.Empty(swept.Ballot);
        Assert.Empty(swept.HandsRaised);
        Assert.NotNull(swept.Sweep);
    }

    [Fact]
    public void Replay_RejectsACountBeforeTheSweepIsComplete()
    {
        // 钟盘形态：收票没走完，计票事件就是损坏的事件流（不等折叠层"替它补收"）。
        var context = DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive));
        var started = StepMachine.StartDay(DayPhaseFixture.Plan(), 1);
        var nominated = DayPhaseFixture.Nominate(started.State, context, nominator: 1, nominee: 2);
        var sweep = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var first = DayPhaseFixture.Collect(sweep.State, context, 1, seat: 1);

        var events = new List<GameEvent>(started.Events);
        events.AddRange(nominated.Events);
        events.AddRange(sweep.Events);
        events.AddRange(first.Events);
        events.Add(new VoteCountedEvent
        {
            DayNumber = 1,
            NominationIndex = 1,
            Voters = [],
            SpentVoteTokens = [],
            AboutToBeExecuted = null,
        });

        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(events));
    }
}
