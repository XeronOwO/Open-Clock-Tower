using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 旅行者与处决路径的接缝（票据 `traveller-and-exile` · R-0049）：旅行者可被提名、票数照记并参与
/// 「当天最多票」比较，但永不进入「即将被处决」；两条处决路径（常规 CloseDay / 处罚处决）拒绝旅行者目标；
/// 旅行者事实缺失时显式拒绝、不猜。
/// </summary>
/// <remarks>
/// 依据：印刷规则书提取文本 · 2026-10-04 抓取 · Travelers（「Travelers are exiled, not executed」）与词汇表
/// （处决 = 杀死非旅行者 / 流放 = 杀死旅行者）；收口口径见 <c>docs/standard/rulings.md</c> R-0049。
/// </remarks>
public sealed class TravellerExecutionTests
{
    [Fact]
    public void Nominate_OnATraveller_IsAccepted()
    {
        var nominated = DayPhaseFixture.Nominate(DayPhaseFixture.StartDay(), Context(), nominator: 1, nominee: 2);

        Assert.Equal(StepMachineOutcomeKind.Applied, nominated.Kind);
    }

    [Fact]
    public void CountVotes_TravellerWithEnoughVotes_DoesNotLand_ButVotesAreRecorded()
    {
        var context = Context();
        var counted = NominateAndCount(context, nominator: 1, nominee: 2, raised: [1, 3]);

        Assert.Equal(StepMachineOutcomeKind.Applied, counted.Kind);
        var voteCounted = Assert.Single(counted.Events.OfType<VoteCountedEvent>());
        Assert.Null(voteCounted.AboutToBeExecuted);

        // 票数照记、进公开面：旅行者不落靶不等于这条提名不存在。
        var nomination = Assert.Single(counted.State.Day!.OpenDay!.Nominations);
        Assert.Equal(2, nomination.Ballot.Count);
    }

    [Fact]
    public void CountVotes_TravellerHighVotes_StillBlockALaterNonTraveller()
    {
        // 4 席：3 号是旅行者，1 / 2 / 4 号是非旅行者。
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "deviant", LifeState.Alive),
            (4, "clockmaker", LifeState.Alive));

        // 旅行者拿 3 票（当天最多）→ 不落靶。
        var first = NominateAndCount(context, nominator: 1, nominee: 3, raised: [1, 2, 4]);
        Assert.Null(Assert.Single(first.Events.OfType<VoteCountedEvent>()).AboutToBeExecuted);

        // 后来的非旅行者只有 2 票：达到半数，但没有超过旅行者的 3 票 → 同样不落靶。
        var second = NominateAndCount(first.State, context, nominator: 2, nominee: 4, raised: [2, 4]);
        Assert.Null(Assert.Single(second.Events.OfType<VoteCountedEvent>()).AboutToBeExecuted);

        // 白天照常收口：没有任何人被处决，也没有死亡事实。
        var closed = DayPhaseFixture.Close(second.State, context);
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);
        Assert.Contains(closed.Events, item => item is DayClosedEvent);
        Assert.DoesNotContain(closed.Events, item => item is ExecutedEvent);
        Assert.DoesNotContain(closed.Events, item => item is SeatStateChangedEvent);
    }

    [Fact]
    public void CloseDay_WithALegacyTravellerCandidate_Rejects()
    {
        var context = Context();
        var counted = NominateAndCount(context, nominator: 1, nominee: 2, raised: [1, 3]);
        Assert.Null(counted.State.Day!.OpenDay!.AboutToBeExecuted);

        // 旧日志 / 损坏流：把旅行者直接写成「即将被处决」——关账必须显式拒绝，不静默杀人（R-0049 第 4 条）。
        var dayState = counted.State.Day!;
        var days = dayState.Days.ToArray();
        days[^1] = days[^1] with { AboutToBeExecuted = new SeatId(2) };
        var legacy = counted.State with
        {
            Day = dayState with { Days = days },
        };

        var closed = DayPhaseFixture.Close(legacy, context);

        Assert.Equal(StepMachineOutcomeKind.Rejected, closed.Kind);
        Assert.Equal("day.execution_target_is_traveller", closed.RejectionCode);
        Assert.Empty(closed.Events);
    }

    [Fact]
    public void PunishExecution_OnATraveller_Rejects()
    {
        var context = Context() with
        {
            AdjudicatedExecutions = [new AlwaysApplicablePunishment()],
        };
        var outcome = DayPhaseFixture.Apply(
            DayPhaseFixture.StartDay(),
            context,
            new PunishExecutionInput
            {
                Seat = new SeatId(2),
                Source = MadnessPunishmentSource.Cerenovus,
                Note = "测试：处罚处决旅行者",
            });

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("punishment.target_is_traveller", outcome.RejectionCode);
        Assert.Empty(outcome.Events);
    }

    [Fact]
    public void CountVotes_WithoutCharacterFacts_RejectsWhenTheVoteWouldLand()
    {
        var context = ExilePhaseFixture.ContextWithoutCharacterFacts(
            (1, "clockmaker", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive));
        var counted = NominateAndCount(context, nominator: 1, nominee: 2, raised: [1, 3]);

        Assert.Equal(StepMachineOutcomeKind.Rejected, counted.Kind);
        Assert.Equal("day.character_facts_missing", counted.RejectionCode);
    }

    [Fact]
    public void CountVotes_WithAnUnobservedNominee_RejectsWhenTheVoteWouldLand()
    {
        var context = ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, null, LifeState.Alive),
            (3, "clockmaker", LifeState.Alive));
        var counted = NominateAndCount(context, nominator: 1, nominee: 2, raised: [1, 3]);

        Assert.Equal(StepMachineOutcomeKind.Rejected, counted.Kind);
        Assert.Equal("day.nominee_character_unknown", counted.RejectionCode);
    }

    /// <summary>3 席夹具：2 号是旅行者（怪咖），1 / 3 号是非旅行者。</summary>
    private static SettlementContext Context() =>
        ExilePhaseFixture.Context(
            (1, "clockmaker", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive));

    private static StepMachineOutcome NominateAndCount(
        SettlementContext context,
        int nominator,
        int nominee,
        params int[] raised) =>
        NominateAndCount(DayPhaseFixture.StartDay(), context, nominator, nominee, raised);

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

    /// <summary>固定「处罚成立」的依据契约替身：本文件只判旅行者排除，不判疯狂规则。</summary>
    private sealed class AlwaysApplicablePunishment : IAdjudicatedExecutionSource
    {
        public MadnessPunishmentSource Source => MadnessPunishmentSource.Cerenovus;

        public AdjudicatedExecutionEligibility Evaluate(
            GameState state,
            IReadOnlyList<SeatId> seats,
            SeatId seat) => new()
            {
                Applicable = true,
                Note = "测试：处罚成立",
                DeathReason = "测试：处罚死亡",
            };
    }
}
