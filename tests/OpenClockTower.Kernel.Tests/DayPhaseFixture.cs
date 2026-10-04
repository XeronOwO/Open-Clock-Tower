using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>白天阶段的共享夹具：构造座次 / 状态账 / 上下文，并走步骤机的白天输入。</summary>
internal static class DayPhaseFixture
{
    /// <summary>白天计划（与生产一致：唯一的 DayWindow 槽位）。</summary>
    internal static StepPlan Plan(int dayNumber = 1) => new()
    {
        Label = $"sv:day-{dayNumber}",
        Phase = GamePhase.Day,
        Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
    };

    /// <summary>按"席位 + 生死"构造状态账（其余维度不观测，白天规则只读生死）。</summary>
    internal static GameState StateOf(params (int Seat, LifeState Life)[] seats)
    {
        var events = seats
            .Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Life = item.Life,
                Reason = "test.setup",
            })
            .ToArray();

        return GameStateMachine.Fold(events);
    }

    /// <summary>构造结算上下文：座次就是给定席位的升序列表。</summary>
    internal static SettlementContext Context(params (int Seat, LifeState Life)[] seats) => new()
    {
        State = StateOf(seats),
        Seats = [.. seats.Select(item => new SeatId(item.Seat)).OrderBy(seat => seat.Value)],
        Abilities = NoAbilities.Instance,
    };

    /// <summary>开一个白天并返回步骤机状态。</summary>
    internal static StepMachineState StartDay(int dayNumber = 1) =>
        StepMachine.StartDay(Plan(dayNumber), dayNumber).State;

    /// <summary>处理一条输入。</summary>
    internal static StepMachineOutcome Apply(StepMachineState state, SettlementContext context, StepMachineInput input) =>
        StepMachine.Handle(state, context, input);

    /// <summary>提名。</summary>
    internal static StepMachineOutcome Nominate(StepMachineState state, SettlementContext context, int nominator, int nominee) =>
        Apply(state, context, new NominateInput { Nominator = new SeatId(nominator), Nominee = new SeatId(nominee) });

    /// <summary>投票 / 撤回。</summary>
    internal static StepMachineOutcome Vote(StepMachineState state, SettlementContext context, int voter, int index, bool voted) =>
        Apply(state, context, new CastVoteInput
        {
            Voter = new SeatId(voter),
            NominationIndex = index,
            Voted = voted,
        });

    /// <summary>计票。</summary>
    internal static StepMachineOutcome Count(StepMachineState state, SettlementContext context, int index) =>
        Apply(state, context, new CountVotesInput { NominationIndex = index });

    /// <summary>结束白天。</summary>
    internal static StepMachineOutcome Close(StepMachineState state, SettlementContext context) =>
        Apply(state, context, new CloseDayInput());

    /// <summary>开始钟盘收票（默认 3s / 1s；R-0017 目标形态）。</summary>
    internal static StepMachineOutcome StartSweep(
        StepMachineState state,
        SettlementContext context,
        int index,
        int countdownMilliseconds = VoteSweepLimits.DefaultCountdownMilliseconds,
        int intervalMilliseconds = VoteSweepLimits.DefaultIntervalMilliseconds) =>
        Apply(state, context, new StartVoteSweepInput
        {
            NominationIndex = index,
            CountdownMilliseconds = countdownMilliseconds,
            IntervalMilliseconds = intervalMilliseconds,
        });

    /// <summary>收第 N 席的票（由控制面按时间轴发出）。</summary>
    internal static StepMachineOutcome Collect(StepMachineState state, SettlementContext context, int index, int seat) =>
        Apply(state, context, new CollectSeatVoteInput
        {
            NominationIndex = index,
            Seat = new SeatId(seat),
        });

    /// <summary>继续中断的收票（重新起倒计时）。</summary>
    internal static StepMachineOutcome ResumeSweep(StepMachineState state, SettlementContext context, int index) =>
        Apply(state, context, new ResumeVoteSweepInput { NominationIndex = index });

    /// <summary>把一项已开始的收票按座次收完（已收过的席位跳过）；返回累积事件与最终状态。</summary>
    internal static StepMachineOutcome CollectAll(StepMachineState state, SettlementContext context, int index)
    {
        var events = new List<GameEvent>();
        var after = state;
        foreach (var seat in context.Seats)
        {
            var alreadyCollected = after.Day?.OpenDay?.OpenNomination?.Sweep?
                .Collected.Any(vote => vote.Seat == seat) ?? false;
            if (alreadyCollected)
            {
                continue;
            }

            var outcome = Collect(after, context, index, seat.Value);
            Assert.True(outcome.Kind == StepMachineOutcomeKind.Applied, $"收票被拒：{outcome.RejectionCode}");
            events.AddRange(outcome.Events);
            after = outcome.State;
        }

        return new StepMachineOutcome
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = after,
            Events = events,
        };
    }

    /// <summary>走完一次钟盘收票：先让给定席位举手，再按座次逐席收完；返回收完后的状态。</summary>
    internal static StepMachineState RunSweep(
        StepMachineState state,
        SettlementContext context,
        int index,
        params int[] raised)
    {
        var started = StartSweep(state, context, index);
        Assert.True(started.Kind == StepMachineOutcomeKind.Applied, $"开始收票被拒：{started.RejectionCode}");
        var after = started.State;

        foreach (var seat in raised)
        {
            var outcome = Vote(after, context, seat, index, voted: true);
            Assert.True(outcome.Kind == StepMachineOutcomeKind.Applied, $"举手被拒：{outcome.RejectionCode}");
            after = outcome.State;
        }

        return CollectAll(after, context, index).State;
    }

    /// <summary>走完收票并计票（最常用的一步到位）。</summary>
    internal static StepMachineOutcome SweepAndCount(
        StepMachineState state,
        SettlementContext context,
        int index,
        params int[] raised) =>
        Count(RunSweep(state, context, index, raised), context, index);

    /// <summary>拒绝码（受理时为 null）。</summary>
    internal static string? CodeOf(StepMachineOutcome outcome) => outcome.RejectionCode;

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
