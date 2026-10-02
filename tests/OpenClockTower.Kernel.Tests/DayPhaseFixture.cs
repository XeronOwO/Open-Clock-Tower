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

    /// <summary>拒绝码（受理时为 null）。</summary>
    internal static string? CodeOf(StepMachineOutcome outcome) => outcome.RejectionCode;

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
