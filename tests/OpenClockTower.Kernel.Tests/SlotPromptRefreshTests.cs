using OpenClockTower.Kernel;
using static OpenClockTower.Kernel.Tests.StepFixture;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 说书人裁定类提示的入槽实时重建：计划快照会漏掉当夜更早槽位的结果，入槽时按
/// 「已提交账 + 本批已产出事件」重建；玩家选项契约不被改写。
/// </summary>
public sealed class SlotPromptRefreshTests
{
    /// <summary>本批结算出的失效先折进重建账，再重建说书人裁定提示。</summary>
    [Fact]
    public void StorytellerDecision_IsRebuiltWithBatchLedger()
    {
        var source = new StubPromptSource();
        var context = Context(source);

        var answered = AnswerFirstSlot(TwoSlotPlan(EmptyPrompt(NoOptionBehavior.StorytellerDecides)), context);

        var raised = Assert.Single(answered.Events.OfType<DecisionPointRaisedEvent>());
        Assert.Equal("重建后的提示", raised.DecisionPoint.Prompt.Context);
        Assert.Equal("重建后的提示", answered.State.CurrentSlot!.Prompt!.Context);

        var request = Assert.Single(source.Requests);
        Assert.Equal(new StepSlotId("slot-b"), request.SlotId);
        Assert.Equal(new CharacterId("test-oracle"), request.Character);
        Assert.Equal(new SeatId(2), request.Actor);

        var malfunction = Assert.Single(request.State.Malfunctions.Entries);
        Assert.Equal(new SeatId(1), malfunction.Seat);
        Assert.Equal(MalfunctionKind.Poisoned, malfunction.Kind);
    }

    /// <summary>没有重建来源（内核夹具 / 只推进不结算）：用计划快照，行为不变。</summary>
    [Fact]
    public void StorytellerDecision_WithoutSource_KeepsSnapshot()
    {
        var context = Context(source: null);

        var answered = AnswerFirstSlot(TwoSlotPlan(EmptyPrompt(NoOptionBehavior.StorytellerDecides)), context);

        var raised = Assert.Single(answered.Events.OfType<DecisionPointRaisedEvent>());
        Assert.Equal("没有合法选项的测试用选择", raised.DecisionPoint.Prompt.Context);
        Assert.Equal("没有合法选项的测试用选择", answered.State.CurrentSlot!.Prompt!.Context);
    }

    /// <summary>重建结果不再是「说书人裁定点」（契约结构变了）：退回计划快照，本步语义不变。</summary>
    [Fact]
    public void RebuiltPromptWithDifferentOutcome_KeepsSnapshot()
    {
        var source = new StubPromptSource(Prompt("重建后反而给了选项"));
        var context = Context(source);

        var answered = AnswerFirstSlot(TwoSlotPlan(EmptyPrompt(NoOptionBehavior.StorytellerDecides)), context);

        Assert.Single(source.Requests);
        var raised = Assert.Single(answered.Events.OfType<DecisionPointRaisedEvent>());
        Assert.Equal("没有合法选项的测试用选择", raised.DecisionPoint.Prompt.Context);
        Assert.Equal("没有合法选项的测试用选择", answered.State.CurrentSlot!.Prompt!.Context);
    }

    /// <summary>入槽重建只作用于说书人裁定点：玩家选项契约保持计划快照，来源不会被问到。</summary>
    [Fact]
    public void PlayerChoiceRequest_IsNotRebuilt()
    {
        var source = new StubPromptSource();
        var context = Context(source);
        var frozen = new ChoicePrompt
        {
            Context = "玩家选择：保持计划快照",
            Options = [new DecisionOption { Value = "option-b", Preview = "选项 B" }],
            OnNoOption = NoOptionBehavior.Skip,
        };

        var answered = AnswerFirstSlot(TwoSlotPlan(frozen), context);

        var issued = Assert.Single(answered.Events.OfType<OperationRequestIssuedEvent>());
        Assert.Equal("玩家选择：保持计划快照", issued.Request.Prompt.Context);
        Assert.Empty(source.Requests);
    }

    /// <summary>开夜 → 配额走完（被挂起挡住）→ 玩家提交；结算与推进（进入第二格）在同一批。</summary>
    private static StepMachineOutcome AnswerFirstSlot(StepPlan plan, SettlementContext context)
    {
        var started = StepMachine.StartPhase(plan, previous: null, ledger: context.State);
        var elapsed = StepMachine.Handle(started.State, context, new SlotQuotaElapsedInput());
        return StepMachine.Handle(
            elapsed.State,
            context,
            new SubmitResponseInput
            {
                RequestId = started.State.PendingRequest!.Id,
                OptionValue = "option-a",
                Source = ResponseSource.Player,
            });
    }

    /// <summary>两格计划：1 号带玩家选项（结算会失败并留下 Poisoned），2 号是说书人裁定格。</summary>
    private static StepPlan TwoSlotPlan(ChoicePrompt secondPrompt) => Plan(
        "test:night-1",
        Action("slot-a", 1, Prompt("option-a"), owner: "test-hero"),
        Action("slot-b", 2, secondPrompt, owner: "test-oracle"));

    private static SettlementContext Context(ISlotPromptSource? source) => new()
    {
        State = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("test-hero"),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Poisoned,
                Reason = "test.setup",
            },
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("test-oracle"),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "test.setup",
            },
        ]),
        Seats = [new SeatId(1), new SeatId(2)],
        Abilities = new TestCatalog(new NoopAbility()),
        SlotPrompts = source,
    };

    private sealed class TestCatalog : IAbilityResolutionCatalog
    {
        private readonly IAbilityResolution _ability;

        internal TestCatalog(IAbilityResolution ability)
        {
            _ability = ability;
        }

        public IAbilityResolution? Find(CharacterId character) =>
            character == _ability.Character ? _ability : null;
    }

    /// <summary>只回答结算结论、不产事件的测试契约（生效判定自带 Poisoned 失效记录）。</summary>
    private sealed class NoopAbility : IAbilityResolution
    {
        public CharacterId Character { get; } = new("test-hero");

        public AbilityId Ability { get; } = new("test-hero");

        public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

        public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context) => [];
    }

    private sealed class StubPromptSource : ISlotPromptSource
    {
        private readonly ChoicePrompt _rebuilt;

        internal StubPromptSource(ChoicePrompt? rebuilt = null) =>
            _rebuilt = rebuilt ?? new ChoicePrompt
            {
                Context = "重建后的提示",
                Options = [],
                OnNoOption = NoOptionBehavior.StorytellerDecides,
            };

        internal List<SlotPromptRequest> Requests { get; } = [];

        public ChoicePrompt? Rebuild(SlotPromptRequest request)
        {
            Requests.Add(request);
            return _rebuilt;
        }
    }
}
