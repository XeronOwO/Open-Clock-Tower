using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 流放账的折叠与重建等价（票据 `traveller-and-exile` · D2）：损坏的事件流必须**显式失败**，
/// 新增字段必须进结构比较器——否则"重建前后一致"会在流放上失明。
/// </summary>
/// <remarks>
/// 依据 D-0010（事件是唯一事实来源、状态是折叠结果）与 D-0014 能力 3（恢复失败不得静默继续）。
/// </remarks>
public sealed class ExileLedgerTests
{
    [Fact]
    public void Fold_RejectsCorruptExileStreams()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "artist", LifeState.Alive));
        var start = StepMachine.StartDay(DayPhaseFixture.Plan(), 1);
        var proposed = ExilePhaseFixture.Propose(start.State, context, 1, 2);
        var sweep = ExilePhaseFixture.StartSweep(proposed.State, context, 1);
        var collectFirst = ExilePhaseFixture.Collect(sweep.State, context, 1, seat: 1);

        // 1) 收票没走完就计票。
        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(
        [
            .. start.Events,
            .. proposed.Events,
            .. sweep.Events,
            .. collectFirst.Events,
            new ExileVoteCountedEvent
            {
                DayNumber = 1,
                ExileIndex = 1,
                Voters = [],
                Conclusion = ExileConclusion.VotesInsufficient,
            },
        ]));

        // 2) 逐席收票不按顺序。
        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(
        [
            .. start.Events,
            .. proposed.Events,
            .. sweep.Events,
            new ExileSeatVoteCollectedEvent
            {
                DayNumber = 1,
                ExileIndex = 1,
                Seat = new SeatId(3),
                Voted = false,
            },
        ]));

        // 3) 钟盘上还有没走完的收票，却又开始第二条收票（提名收票进行中 + 伪造流放开始事件）。
        var nominated = DayPhaseFixture.Nominate(start.State, context, nominator: 1, nominee: 2);
        var nominationSweep = DayPhaseFixture.StartSweep(nominated.State, context, 1);
        var proposedDuringNomination = ExilePhaseFixture.Propose(nominationSweep.State, context, 1, 2);
        var corruptDial = Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(
        [
            .. start.Events,
            .. nominated.Events,
            .. nominationSweep.Events,
            .. proposedDuringNomination.Events,
            new ExileSweepStartedEvent
            {
                DayNumber = 1,
                ExileIndex = 1,
                Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
                CountdownMilliseconds = ExilePhaseFixture.DefaultCountdownMilliseconds,
                IntervalMilliseconds = ExilePhaseFixture.DefaultIntervalMilliseconds,
            },
        ]));
        Assert.Contains("钟盘", corruptDial.Message, StringComparison.Ordinal);

        // 4) 计票名单与已折叠的票面不一致（票面为空，计票名单却有一票）。
        var collected = ExilePhaseFixture.CollectAll(sweep.State, context, 1);
        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(
        [
            .. start.Events,
            .. proposed.Events,
            .. sweep.Events,
            .. collected.Events,
            new ExileVoteCountedEvent
            {
                DayNumber = 1,
                ExileIndex = 1,
                Voters = [new SeatId(1)],
                Conclusion = ExileConclusion.VotesInsufficient,
            },
        ]));

        // 5) 同一条流放重复计票。
        var counted = ExilePhaseFixture.Count(collected.State, context, 1);
        var validStream = new List<GameEvent>(start.Events);
        validStream.AddRange(proposed.Events);
        validStream.AddRange(sweep.Events);
        validStream.AddRange(collected.Events);
        validStream.AddRange(counted.Events);
        validStream.Add(counted.Events.OfType<ExileVoteCountedEvent>().Single());
        Assert.Throws<InvalidOperationException>(() => StepMachine.Fold(validStream));
    }

    [Fact]
    public void Comparer_SeesTheExileLedger()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "barista", LifeState.Alive),
            (3, "deviant", LifeState.Alive));
        var proposed = ExilePhaseFixture.Propose(ExilePhaseFixture.StartDay(), context, 1, 2);
        var left = proposed.State;
        var tampered = left with
        {
            Day = left.Day! with
            {
                Days =
                [
                    left.Day!.Days[0] with
                    {
                        Exiles = [left.Day!.Days[0].Exiles[0] with { Target = new SeatId(3) }],
                    },
                ],
            },
        };

        Assert.True(StepMachineStateComparer.AreEquivalent(left, left with { }));
        Assert.False(StepMachineStateComparer.AreEquivalent(left, tampered));
    }
}
