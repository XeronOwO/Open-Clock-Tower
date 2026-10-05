using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 博学者白天提问的状态机（平台口径见 <c>docs/standard/rulings.md</c> R-0057）：
/// 提问走玩家主动输入、开归属席位的裁定点；两条信息的结清、每个白天一次、强推作废与跨阶段收口。
/// </summary>
/// <remarks>
/// 规则语义（谁是博学者、两条信息的格式）由注入的 <see cref="ISavantQuestionSource"/> 给出——
/// 这里是内核夹具：只验证状态机形状、事件顺序与拒绝路径。
/// </remarks>
public sealed class SavantQuestionMachineTests
{
    private static readonly SeatId Savant = new(1);
    private static readonly DecisionPointId DecisionId = new("sv:day-1:savant.question");

    /// <summary>提问：请求与裁定点同批进事件流；裁定点归属提问席位、由白天窗口槽位承载。</summary>
    [Fact]
    public void Ask_OpensQuestionAndDecision_WithAttribution()
    {
        var state = Day();

        var outcome = Ask(state, Context());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var asked = Assert.Single(outcome.Events.OfType<SavantQuestionAskedEvent>());
        Assert.Equal(Savant, asked.Seat);
        Assert.Equal(new CharacterId("savant"), asked.Character);

        var raised = Assert.Single(outcome.Events.OfType<DecisionPointRaisedEvent>());
        Assert.Equal(Savant, raised.AttributionSeat);
        Assert.Equal(state.CurrentSlot!.Id, raised.SlotId);
        Assert.Equal(DecisionId, raised.DecisionPoint.Id);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, raised.DecisionPoint.Prompt.OnNoOption);

        Assert.NotNull(outcome.State.SavantQuestion);
        Assert.Equal(Savant, outcome.State.SavantQuestion!.Seat);
        Assert.Equal(Savant, outcome.State.SavantAskedSeat);
        Assert.NotNull(outcome.State.AwaitingDecision);
    }

    /// <summary>已有未结清的提问：第二次要信息被拒（不给"连要"留口子）。</summary>
    [Fact]
    public void Ask_RejectsSecondQuestionWhilePending()
    {
        var pending = Ask(Day(), Context()).State;

        var second = Ask(pending, Context());

        Assert.Equal(StepMachineOutcomeKind.Rejected, second.Kind);
        Assert.Equal("savant.question_pending", second.RejectionCode);
        Assert.Empty(second.Events);
    }

    /// <summary>不是白天：要信息被拒（百科《博学者》· 角色能力：每个白天）。</summary>
    [Fact]
    public void Ask_RejectsOutsideDay()
    {
        var night = StepMachine.StartPhase(
            new StepPlan
            {
                Label = "sv:night-1",
                Phase = GamePhase.FirstNight,
                Slots = [StepFixture.Beat("dusk")],
            },
            ControlMode.Automatic).State;

        var outcome = Ask(night, Context());

        Assert.Equal("savant.not_open_day", outcome.RejectionCode);
    }

    /// <summary>该席位不是博学者：找不到提问来源契约，显式拒绝。</summary>
    [Fact]
    public void Ask_RejectsNonSavantSeat()
    {
        var outcome = Ask(Day(), Context(character: "clockmaker"));

        Assert.Equal("savant.not_savant", outcome.RejectionCode);
    }

    /// <summary>每个白天一次：同一天已经要过之后不能再来一次（R-0057）。</summary>
    [Fact]
    public void Ask_RejectsSecondTimeInTheSameDay()
    {
        var answered = Resolve(Ask(Day(), Context()).State, Context(), "3 号是镇民|5 号是爪牙").State;

        var again = Ask(answered, Context());

        Assert.Equal("savant.already_asked_today", again.RejectionCode);
    }

    /// <summary>新白天可以再要一次：「今天要过」的账不跨阶段携带。</summary>
    [Fact]
    public void Ask_AllowsAgainOnTheNextDay()
    {
        var context = Context();
        var answered = Resolve(Ask(Day(), context).State, context, "3 号是镇民|5 号是爪牙").State;

        // 先正常关掉第一天，再开第二天（白天账要求一天一天走）。
        var closed = StepMachine.Handle(answered, context, new CloseDayInput());
        Assert.Equal(StepMachineOutcomeKind.Applied, closed.Kind);

        var nextDay = StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-2",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 2,
            previous: closed.State).State;

        Assert.Null(nextDay.SavantAskedSeat);
        Assert.Equal(StepMachineOutcomeKind.Applied, Ask(nextDay, context).Kind);
    }

    /// <summary>结清：裁定点收口、提问清空、记账一次、两条信息只发给本人。</summary>
    [Fact]
    public void Resolve_ClosesQuestion_AndIssuesTwoResults()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = Resolve(asked, Context(), "3 号是镇民|5 号是爪牙");

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var closed = Assert.Single(outcome.Events.OfType<SavantQuestionClosedEvent>());
        Assert.Equal(Savant, closed.Seat);
        Assert.Equal(SavantQuestionClosure.Answered, closed.Closure);

        var resolved = Assert.Single(outcome.Events.OfType<DecisionPointResolvedEvent>());
        Assert.Equal("3 号是镇民|5 号是爪牙", resolved.Decision);

        var use = Assert.Single(outcome.Events.OfType<AbilityResolvedEvent>());
        Assert.Equal(Savant, use.Actor);
        Assert.Equal(new AbilityId("savant"), use.Ability);
        Assert.True(use.Effective);

        var results = outcome.Events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Equal(Savant, result.Recipient));
        Assert.Null(outcome.State.SavantQuestion);
        Assert.NotNull(outcome.State.SavantAskedSeat);
    }

    /// <summary>没有给出内容：显式拒绝（不猜、也不落一条空信息）。</summary>
    [Fact]
    public void Resolve_RejectsMissingDecision()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = StepMachine.Handle(
            asked,
            Context(),
            new ResolveDecisionPointInput { DecisionPointId = DecisionId, Decision = "  " });

        Assert.Equal("savant.decision_missing", outcome.RejectionCode);
        Assert.NotNull(outcome.State.SavantQuestion);
    }

    /// <summary>裁定文本不合格式：按契约结论显式拒绝（自由文本是用户输入，不抛异常）。</summary>
    [Fact]
    public void Resolve_RejectsInvalidDecisionFormat()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = Resolve(asked, Context(), "只有一条");

        Assert.Equal("savant.decision_invalid", outcome.RejectionCode);
        Assert.NotNull(outcome.State.SavantQuestion);
    }

    /// <summary>判不了（席位维度没观测齐）：显式拒绝，提问仍挂着（不猜，D-0015）。</summary>
    [Fact]
    public void Resolve_Indeterminate_IsRejected()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = Resolve(asked, Context(indeterminate: true), "3 号是镇民|5 号是爪牙");

        Assert.Equal("savant.indeterminate", outcome.RejectionCode);
        Assert.NotNull(outcome.State.SavantQuestion);
    }

    /// <summary>强推收口白天：未结清的提问显式作废（不记账、不产信息），不留到下一阶段。</summary>
    [Fact]
    public void ForceAdvance_AbandonsPendingQuestion()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = StepMachine.Handle(
            asked,
            Context(),
            new ForceAdvanceInput { Reason = "测试：强推收口" });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(outcome.Events, gameEvent => gameEvent is SavantQuestionClosedEvent
        {
            Seat: var seat,
            Closure: SavantQuestionClosure.Abandoned,
        } && seat == Savant);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is InformationResultIssuedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is AbilityResolvedEvent);
    }

    /// <summary>未结清时不能关账：先给出两条信息（或强推作废）。</summary>
    [Fact]
    public void CloseDay_WhilePending_IsRejected()
    {
        var asked = Ask(Day(), Context()).State;

        var outcome = StepMachine.Handle(asked, Context(), new CloseDayInput());

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("day.savant_question_pending", outcome.RejectionCode);
    }

    private static StepMachineOutcome Ask(StepMachineState state, SettlementContext context) =>
        StepMachine.Handle(state, context, new AskSavantQuestionInput { Seat = Savant });

    private static StepMachineOutcome Resolve(
        StepMachineState state,
        SettlementContext context,
        string decision) =>
        StepMachine.Handle(state, context, new ResolveDecisionPointInput
        {
            DecisionPointId = DecisionId,
            Decision = decision,
        });

    /// <summary>白天步骤机状态（唯一 DayWindow 槽位，与生产一致）。</summary>
    private static StepMachineState Day() =>
        StepMachine.StartDay(
            new StepPlan
            {
                Label = "sv:day-1",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
            },
            dayNumber: 1).State;

    /// <summary>结算上下文：1 号是指定角色；<paramref name="indeterminate"/> 时缺醉酒 / 中毒观测。</summary>
    private static SettlementContext Context(string character = "savant", bool indeterminate = false)
    {
        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                Seat = Savant,
                Life = LifeState.Alive,
                Character = new CharacterId(character),
                Drunk = indeterminate ? null : DrunkState.Sober,
                Poison = indeterminate ? null : PoisonState.Healthy,
                Reason = "test.setup",
            },
        };

        return new SettlementContext
        {
            State = GameStateMachine.Fold(events),
            Seats = [Savant, new SeatId(2)],
            Abilities = NoAbilities.Instance,
            SavantQuestions = [new FakeSource()],
        };
    }

    /// <summary>测试用提问来源：要求用 <c>|</c> 分成两条；不合法给 Invalid；缺维度给 null。</summary>
    private sealed class FakeSource : ISavantQuestionSource
    {
        public CharacterId Character => new("savant");

        public AbilityId Ability => new("savant");

        public ChoicePrompt BuildPrompt(SavantPromptContext context) => new()
        {
            Context = $"测试：请给两条信息（{context.Seat.Value} 号）",
            Options = [],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };

        public SavantQuestionResolution? Resolve(SavantQuestionResolutionContext context)
        {
            var entry = context.State.Seat(context.Question.Seat);
            if (entry?.DrunkValue is null || entry.PoisonValue is null)
            {
                return null;
            }

            var parts = (context.Decision ?? string.Empty).Split('|');
            if (parts.Length != 2)
            {
                return new SavantQuestionResolution
                {
                    Ruling = SavantQuestionRuling.Invalid,
                    Note = "测试：两条信息要用 | 分开",
                };
            }

            return new SavantQuestionResolution
            {
                Ruling = SavantQuestionRuling.Answered,
                Effective = true,
                Events =
                [
                    new InformationResultIssuedEvent
                    {
                        Recipient = context.Question.Seat,
                        Ability = Ability,
                        Content = parts[0],
                        MayBeFalse = true,
                    },
                    new InformationResultIssuedEvent
                    {
                        Recipient = context.Question.Seat,
                        Ability = Ability,
                        Content = parts[1],
                        MayBeFalse = true,
                    },
                ],
            };
        }
    }

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
