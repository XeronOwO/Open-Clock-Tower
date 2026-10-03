using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 「使用即消耗」的记账边界（R-0036 / R-0040）：摇头 / 不用这类选择**不记**能力使用账本——
/// 记了会让建表期误判「机会已浪费」，把之后的夜晚一并吞掉。
/// </summary>
/// <remarks>
/// 这是哲学家（每局限一次「获得能力」）与女裁缝（每局限一次信息）共用的边界；
/// 修复前的实现把摇头也记成一次使用，回归证据见本用例与 Rules 的 <c>CountsAsUse</c> 断言。
/// </remarks>
public sealed class OnceAbilityUseTests
{
    /// <summary>摇头（CountsAsUse=false）：结算产出里没有 AbilityResolvedEvent，账本保持干净。</summary>
    [Fact]
    public void Decline_DoesNotRecordAbilityUse()
    {
        var outcome = Respond("decline");

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Empty(outcome.Events.OfType<AbilityResolvedEvent>());

        var ledger = GameStateMachine.Fold(outcome.Events);
        Assert.False(ledger.AbilityUses.WasUsed(new SeatId(1), new AbilityId("test.once")));
    }

    /// <summary>真选（CountsAsUse=true）：记一条使用；未生效的使用也照记（三-3「机会被浪费」）。</summary>
    [Fact]
    public void RealChoice_RecordsAbilityUse()
    {
        var outcome = Respond("use");

        var resolved = Assert.Single(outcome.Events.OfType<AbilityResolvedEvent>());
        Assert.Equal(new SeatId(1), resolved.Actor);
        Assert.Equal(new AbilityId("test.once"), resolved.Ability);

        var ledger = GameStateMachine.Fold(outcome.Events);
        Assert.True(ledger.AbilityUses.WasUsed(new SeatId(1), new AbilityId("test.once")));
    }

    /// <summary>经完整响应路径：玩家在一个一次性能力槽位上作答，结算按契约的 CountsAsUse 决定记账。</summary>
    private static StepMachineOutcome Respond(string choice)
    {
        var request = new OperationRequest
        {
            Id = new OperationRequestId("test.once:1"),
            Addressee = new SeatId(1),
            Origin = OperationRequestOrigin.ForSlot(new StepSlotId("test-slot"), "test:night-1", 0),
            Prompt = new ChoicePrompt
            {
                Context = "测试：一次性能力",
                Options =
                [
                    new DecisionOption { Value = "use", Preview = "使用" },
                    new DecisionOption { Value = "decline", Preview = "摇头" },
                ],
                OnNoOption = NoOptionBehavior.BlockAndAlert,
            },
        };

        var plan = new StepPlan
        {
            Label = "test:night-1",
            Phase = GamePhase.FirstNight,
            Slots = [StepFixture.Action("test-slot", 1, owner: "test.once")],
        };
        var state = StepMachine.StartPhase(plan).State with { PendingRequest = request };

        var ledger = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
        ]);

        return StepMachine.Handle(
            state,
            new SettlementContext
            {
                State = ledger,
                Seats = [new SeatId(1)],
                Abilities = new FakeCatalog(),
            },
            new SubmitResponseInput
            {
                RequestId = request.Id,
                OptionValue = choice,
                Source = ResponseSource.Player,
            });
    }

    /// <summary>测试契约：只有真选才算使用（摇头不记）。</summary>
    private sealed class FakeOnceAbility : IAbilityResolution
    {
        public CharacterId Character => new("test");

        public AbilityId Ability { get; } = new("test.once");

        public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

        public bool CountsAsUse(AbilityResolutionContext context) =>
            !string.Equals(context.Choice, "decline", StringComparison.Ordinal);

        public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context) => [];
    }

    private sealed class FakeCatalog : IAbilityResolutionCatalog
    {
        private readonly FakeOnceAbility _ability = new();

        public IAbilityResolution? Find(CharacterId character) =>
            character.Value == "test.once" ? _ability : null;
    }
}
