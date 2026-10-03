using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 艺术家白天提问的状态机（平台口径见 <c>docs/standard/rulings.md</c> R-0040）：
/// 提问走玩家主动输入、开归属席位的裁定点；回答 / 要求重问的结清、强推作废与跨阶段收口。
/// </summary>
/// <remarks>
/// 规则语义（谁是艺术家、回答文案）由注入的 <see cref="IArtistQuestionSource"/> 给出——
/// 这里是内核夹具：只验证状态机形状、事件顺序与拒绝路径。
/// </remarks>
public sealed class ArtistQuestionMachineTests
{
    private static readonly SeatId Artist = new(1);
    private static readonly DecisionPointId DecisionId = new("sv:day-1:artist.question");

    /// <summary>提问：问题与裁定点同批进事件流；裁定点归属提问席位、由白天窗口槽位承载。</summary>
    [Fact]
    public void Ask_OpensQuestionAndDecision_WithAttribution()
    {
        var state = Day();

        var outcome = Ask(state, Context());

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var asked = Assert.Single(outcome.Events.OfType<ArtistQuestionAskedEvent>());
        Assert.Equal(Artist, asked.Seat);
        Assert.Equal(new CharacterId("artist"), asked.Character);
        Assert.Equal("2 号是爪牙吗？", asked.Question);

        var raised = Assert.Single(outcome.Events.OfType<DecisionPointRaisedEvent>());
        Assert.Equal(Artist, raised.AttributionSeat);
        Assert.Equal(state.CurrentSlot!.Id, raised.SlotId);
        Assert.Null(raised.TriggerAbility);
        Assert.Equal(DecisionId, raised.DecisionPoint.Id);
        Assert.Equal(2, raised.DecisionPoint.Prompt.Options.Count);

        Assert.NotNull(outcome.State.ArtistQuestion);
        Assert.Equal("2 号是爪牙吗？", outcome.State.ArtistQuestion!.Question);
        Assert.NotNull(outcome.State.AwaitingDecision);
    }

    /// <summary>已有未结清的问题：第二次提问被拒（不给"连问"留口子）。</summary>
    [Fact]
    public void Ask_RejectsSecondQuestionWhilePending()
    {
        var pending = Ask(Day(), Context()).State;

        var second = Ask(pending, Context());

        Assert.Equal(StepMachineOutcomeKind.Rejected, second.Kind);
        Assert.Equal("artist.question_pending", second.RejectionCode);
        Assert.Empty(second.Events);
    }

    /// <summary>不是白天：提问被拒（R-0040 第 2 条：只能在白天进行）。</summary>
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

        Assert.Equal("artist.not_open_day", outcome.RejectionCode);
    }

    /// <summary>该席位不是艺术家：找不到提问来源契约，显式拒绝。</summary>
    [Fact]
    public void Ask_RejectsNonArtistSeat()
    {
        var outcome = Ask(Day(), Context(character: "clockmaker"));

        Assert.Equal("artist.not_artist", outcome.RejectionCode);
    }

    /// <summary>每局限一次：用过（含未生效）之后不能再提问（R-0040 第 1 条）。</summary>
    [Fact]
    public void Ask_RejectsUsedAbility()
    {
        var outcome = Ask(Day(), Context(used: true));

        Assert.Equal("artist.already_used", outcome.RejectionCode);
    }

    /// <summary>空问题 / 超长问题 / 控制字符显式拒绝；问题文本先 trim。</summary>
    [Fact]
    public void Ask_RejectsEmptyAndTooLong()
    {
        Assert.Equal("artist.question_empty", Ask(Day(), Context(), "   ").RejectionCode);
        Assert.Equal(
            "artist.question_too_long",
            Ask(Day(), Context(), new string('问', 201)).RejectionCode);
        Assert.Equal(
            "artist.question_control",
            Ask(Day(), Context(), "2 号是爪牙吗？\u0007").RejectionCode);
        Assert.Equal(
            "artist.question_control",
            Ask(Day(), Context(), "两行\n问题").RejectionCode);

        var trimmed = Ask(Day(), Context(), "  2 号是爪牙吗？  ").State.ArtistQuestion;
        Assert.Equal("2 号是爪牙吗？", trimmed!.Question);
    }

    /// <summary>回答结清：清裁定点、清问题、记一次使用、下发信息（顺序固定）。</summary>
    [Fact]
    public void Resolve_Answered_ClosesAndRecordsUse()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        var outcome = Resolve(pending, context, "yes");

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Collection(
            outcome.Events,
            item => Assert.IsType<DecisionPointResolvedEvent>(item),
            item =>
            {
                var closed = Assert.IsType<ArtistQuestionClosedEvent>(item);
                Assert.Equal(ArtistQuestionClosure.Answered, closed.Closure);
            },
            item =>
            {
                var resolved = Assert.IsType<AbilityResolvedEvent>(item);
                Assert.Equal(Artist, resolved.Actor);
                Assert.Equal(new AbilityId("artist"), resolved.Ability);
            },
            item => Assert.IsType<InformationResultIssuedEvent>(item));
        Assert.Null(outcome.State.ArtistQuestion);
        Assert.Null(outcome.State.AwaitingDecision);
    }

    /// <summary>要求重问：只清问题，不记使用、不下发信息（R-0040 第 4 条）。</summary>
    [Fact]
    public void Resolve_Returned_ClosesWithoutUse()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        var outcome = Resolve(pending, context, "retry");

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var closed = Assert.Single(outcome.Events.OfType<ArtistQuestionClosedEvent>());
        Assert.Equal(ArtistQuestionClosure.Returned, closed.Closure);
        Assert.Empty(outcome.Events.OfType<AbilityResolvedEvent>());
        Assert.Empty(outcome.Events.OfType<InformationResultIssuedEvent>());
        Assert.Null(outcome.State.ArtistQuestion);
    }

    /// <summary>
    /// 提问后席位角色被换走：结清仍按**提问时的角色快照**取来源，不会卡死在 source_unknown
    /// （独立对抗性复核 M-1：否则只剩强推作废一条路）。
    /// </summary>
    [Fact]
    public void Resolve_AfterSeatCharacterChanges_UsesQuestionSnapshot()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;
        Assert.Equal(new CharacterId("artist"), pending.ArtistQuestion!.Character);

        // 挂起期间 1 号已被换成别的角色：结清读的是问题里的快照，而不是"当前角色"。
        var changed = Context(character: "clockmaker");
        var outcome = Resolve(pending, changed, "yes");

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Null(outcome.State.ArtistQuestion);
        Assert.Null(outcome.State.AwaitingDecision);
        Assert.Single(outcome.Events.OfType<InformationResultIssuedEvent>());
    }

    /// <summary>说书人强推白天：挂起的问题显式作废（Abandoned），不记账、不留到下一阶段。</summary>
    [Fact]
    public void ForceAdvance_AbandonsPendingQuestion()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        var outcome = StepMachine.Handle(
            pending,
            context,
            new ForceAdvanceInput { Reason = "测试：强推越过提问" });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var closed = Assert.Single(outcome.Events.OfType<ArtistQuestionClosedEvent>());
        Assert.Equal(ArtistQuestionClosure.Abandoned, closed.Closure);
        Assert.Contains(outcome.Events.OfType<DecisionPointResolvedEvent>(), item => item.Decision is null);
        Assert.Empty(outcome.Events.OfType<AbilityResolvedEvent>());
        Assert.True(outcome.State.IsPlanCompleted);
    }

    /// <summary>问题未结清时不能结束白天（内核保护；推进闸在应用层另有一道）。</summary>
    [Fact]
    public void CloseDay_IsRejectedWhileQuestionPending()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        var outcome = StepMachine.Handle(pending, context, new CloseDayInput());

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("day.artist_question_pending", outcome.RejectionCode);
    }

    /// <summary>问题不允许跨阶段顺延：边界上仍挂着即事件流损坏（显式失败）。</summary>
    [Fact]
    public void Question_CannotCarryAcrossPhase()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.StartPhase(
            new StepPlan
            {
                Label = "sv:night-2",
                Phase = GamePhase.OtherNight,
                Slots = [StepFixture.Beat("dusk")],
            },
            pending,
            context.State));
    }

    /// <summary>重复结清 / 席位对不上都是事件流损坏（折叠必须失败，不静默继续）。</summary>
    [Fact]
    public void Folder_RejectsMismatchedClosure()
    {
        var context = Context();
        var pending = Ask(Day(), context).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            pending,
            new ArtistQuestionClosedEvent { Seat = new SeatId(2), Closure = ArtistQuestionClosure.Answered }));
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            pending,
            new ArtistQuestionAskedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("artist"),
                Question = "第二条",
            }));
    }

    /// <summary>比较器看得到进行中问题：问题全文或角色快照不同 = 不等价（重建校验不能在这里失明）。</summary>
    [Fact]
    public void Comparer_SeesQuestionChanges()
    {
        var state = Ask(Day(), Context()).State;
        var other = state with
        {
            ArtistQuestion = new ArtistQuestion
            {
                Seat = Artist,
                Character = new CharacterId("artist"),
                Question = "换了一个问题",
            },
        };
        var changedCharacter = state with
        {
            ArtistQuestion = new ArtistQuestion
            {
                Seat = Artist,
                Character = new CharacterId("clockmaker"),
                Question = "2 号是爪牙吗？",
            },
        };

        Assert.False(StepMachineStateComparer.AreEquivalent(state, other));
        Assert.False(StepMachineStateComparer.AreEquivalent(state, changedCharacter));
    }

    private static StepMachineOutcome Ask(
        StepMachineState state,
        SettlementContext context,
        string question = "2 号是爪牙吗？") =>
        StepMachine.Handle(state, context, new AskArtistQuestionInput { Seat = Artist, Question = question });

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

    /// <summary>结算上下文：1 号是指定角色；<paramref name="used"/> 预置一条使用记录。</summary>
    private static SettlementContext Context(string character = "artist", bool used = false)
    {
        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                Seat = Artist,
                Life = LifeState.Alive,
                Character = new CharacterId(character),
                Reason = "test.setup",
            },
        };
        if (used)
        {
            events.Add(new AbilityResolvedEvent
            {
                SlotId = new StepSlotId("day-window"),
                Actor = Artist,
                Ability = new AbilityId("artist"),
                Effective = true,
            });
        }

        return new SettlementContext
        {
            State = GameStateMachine.Fold(events),
            Seats = [Artist, new SeatId(2)],
            Abilities = NoAbilities.Instance,
            ArtistQuestions = [new FakeSource()],
        };
    }

    /// <summary>测试用提问来源：回答「是」或要求重问；文案不参与断言。</summary>
    private sealed class FakeSource : IArtistQuestionSource
    {
        public CharacterId Character => new("artist");

        public AbilityId Ability => new("artist");

        public ChoicePrompt BuildPrompt(string question) => new()
        {
            Context = $"测试问题：{question}",
            Options =
            [
                new DecisionOption { Value = "yes", Preview = "是" },
                new DecisionOption { Value = "retry", Preview = "要求重问" },
            ],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };

        public ArtistQuestionResolution? Resolve(ArtistQuestionResolutionContext context) =>
            context.Decision == "retry"
                ? new ArtistQuestionResolution
                {
                    Ruling = ArtistQuestionRuling.Returned,
                    Note = "测试：要求重问",
                }
                : new ArtistQuestionResolution
                {
                    Ruling = ArtistQuestionRuling.Answered,
                    Effective = true,
                    Events =
                    [
                        new InformationResultIssuedEvent
                        {
                            Recipient = context.Question.Seat,
                            Ability = Ability,
                            Content = "是",
                            MayBeFalse = false,
                        },
                    ],
                };
    }

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
