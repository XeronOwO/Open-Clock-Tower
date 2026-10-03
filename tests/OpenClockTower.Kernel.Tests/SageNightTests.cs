using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 贤者「被恶魔杀死」事实的机器级生命周期（平台口径见 <c>docs/standard/rulings.md</c> R-0038）：
/// 死亡批开事实、当夜格消费后关闭、夜晚走完未消费显式「过时不候」、事实不允许跨阶段顺延；
/// 触发型裁定点的来源表达与清空。
/// </summary>
public sealed class SageNightTests
{
    /// <summary>事实开启 / 关闭；重复开启与无开启关闭都显式失败。</summary>
    [Fact]
    public void Fact_OpensAndCloses_AndCannotRepeat()
    {
        var started = Start(NightPlan(StepFixture.Beat("dusk"), Trigger("sage")));
        var opened = StepMachine.Apply(started.State, Open(2));

        Assert.NotNull(opened!.SageNight);
        Assert.Equal(new SeatId(2), opened.SageNight!.Sage);
        Assert.Equal(new SeatId(5), opened.SageNight.Demon);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(opened, Open(2)));

        var closed = StepMachine.Apply(opened, new SageNightClosedEvent { Note = "展示完成" });
        Assert.Null(closed!.SageNight);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            closed,
            new SageNightClosedEvent { Note = "重复关闭" }));
    }

    /// <summary>夜晚走完仍未消费：显式记「过时不候」再清空。</summary>
    [Fact]
    public void UnconsumedFact_IsClosedAtNightEnd_WithExpiryNote()
    {
        var started = Start(NightPlan(StepFixture.Beat("dusk"), Trigger("sage"), StepFixture.Beat("dawn")));
        var state = StepMachine.Apply(started.State, Open(2))!;

        var lastEvents = new List<GameEvent>();
        for (var attempt = 0; attempt < 4 && !state.IsPlanCompleted; attempt++)
        {
            var outcome = StepMachine.Handle(state, new SlotQuotaElapsedInput());
            Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
            lastEvents = [.. outcome.Events];
            state = outcome.State;
        }

        Assert.True(state.IsPlanCompleted);
        Assert.Null(state.SageNight);
        var closed = Assert.Single(lastEvents.OfType<SageNightClosedEvent>());
        Assert.Contains("过时不候", closed.Note, StringComparison.Ordinal);
    }

    /// <summary>事实不允许从夜晚原样带进新阶段（夜末收口缺失必须显式失败，不静默顺延）。</summary>
    [Fact]
    public void Fact_CannotCarryOutOfNight()
    {
        var started = Start(NightPlan(StepFixture.Beat("dusk"), StepFixture.Beat("dawn")));
        var withFact = StepMachine.Apply(started.State, Open(2));

        Assert.Throws<InvalidOperationException>(() => StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk")),
            withFact,
            GameState.Empty));
    }

    /// <summary>触发型裁定点：来源写进挂起状态；进入下一个槽位时连同裁定点一起清空。</summary>
    [Fact]
    public void TriggerSourcedDecision_RegistersOrigin_AndSlotEntryClears()
    {
        var started = Start(NightPlan(Trigger("sage"), StepFixture.Beat("dawn")));
        var raised = StepMachine.Apply(started.State, new DecisionPointRaisedEvent
        {
            SlotId = null,
            TriggerAbility = new AbilityId("sweetheart"),
            AttributionSeat = new SeatId(3),
            DecisionPoint = new DecisionPoint
            {
                Id = new DecisionPointId("sweetheart:1"),
                Prompt = StepFixture.Prompt("seat:1"),
            },
        })!;

        Assert.NotNull(raised.AwaitingDecision);
        Assert.Equal(new AbilityId("sweetheart"), raised.AwaitingDecisionTriggerAbility);
        Assert.Equal(new SeatId(3), raised.AwaitingDecisionSeat);

        var entered = StepMachine.Apply(raised, new SlotEnteredEvent
        {
            SlotIndex = 1,
            SlotId = new StepSlotId("dawn"),
        })!;
        Assert.Null(entered.AwaitingDecision);
        Assert.Null(entered.AwaitingDecisionTriggerAbility);
        Assert.Null(entered.AwaitingDecisionSeat);
    }

    /// <summary>裁定点来源必须恰好一个：都缺 / 都填都是事件流损坏（显式失败）。</summary>
    [Fact]
    public void DecisionSource_MustBeExactlyOne()
    {
        var started = Start(NightPlan(Trigger("sage")));

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            started.State,
            new DecisionPointRaisedEvent
            {
                DecisionPoint = new DecisionPoint
                {
                    Id = new DecisionPointId("d:1"),
                    Prompt = StepFixture.Prompt("seat:1"),
                },
            }));

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            started.State,
            new DecisionPointRaisedEvent
            {
                SlotId = new StepSlotId("sage"),
                TriggerAbility = new AbilityId("sage"),
                DecisionPoint = new DecisionPoint
                {
                    Id = new DecisionPointId("d:2"),
                    Prompt = StepFixture.Prompt("seat:1"),
                },
            }));
    }

    /// <summary>
    /// 旧版本事件流没有归属席位字段：重放**容忍为 null**（后加的可空字段不能把旧事件流打断），
    /// 界面退回行动者 / 摘要回退；新开点一律显式给出，由 5 处开点的用例锁住。
    /// </summary>
    [Fact]
    public void MissingAttributionSeat_IsToleratedForOldStreams()
    {
        var started = Start(NightPlan(Trigger("sage")));

        var oldShape = StepMachine.Apply(started.State, new DecisionPointRaisedEvent
        {
            SlotId = new StepSlotId("sage"),
            DecisionPoint = new DecisionPoint
            {
                Id = new DecisionPointId("d:3"),
                Prompt = StepFixture.Prompt("seat:1"),
            },
        });

        Assert.NotNull(oldShape);
        Assert.NotNull(oldShape!.AwaitingDecision);
        Assert.Null(oldShape.AwaitingDecisionSeat);
    }

    /// <summary>比较器看得见贤者事实与触发来源：重建校验不能在这两处失明。</summary>
    [Fact]
    public void Comparer_SeesFactAndTriggerOrigin()
    {
        var started = Start(NightPlan(Trigger("sage")));
        var withFact = StepMachine.Apply(started.State, Open(2));
        Assert.False(StepMachineStateComparer.AreEquivalent(started.State, withFact));

        var decision = StepMachine.Apply(started.State, new DecisionPointRaisedEvent
        {
            SlotId = null,
            TriggerAbility = new AbilityId("sage"),
            AttributionSeat = new SeatId(2),
            DecisionPoint = new DecisionPoint
            {
                Id = new DecisionPointId("d:1"),
                Prompt = StepFixture.Prompt("seat:1"),
            },
        });
        var withoutOrigin = decision! with { AwaitingDecisionTriggerAbility = null };
        Assert.False(StepMachineStateComparer.AreEquivalent(decision, withoutOrigin));

        // 归属席位同样进比较器：重建校验不能在"谁在等"上失明。
        var withoutSeat = decision! with { AwaitingDecisionSeat = null };
        Assert.False(StepMachineStateComparer.AreEquivalent(decision, withoutSeat));
        var otherSeat = decision! with { AwaitingDecisionSeat = new SeatId(9) };
        Assert.False(StepMachineStateComparer.AreEquivalent(decision, otherSeat));
    }

    /// <summary>
    /// 触发型裁定结清：内核只落裁定本身，**不**自己产槽位事件——结清后的续推由提交管线补
    /// （`SessionCommit.AppendDecisionContinuation`：重进本格复位配额；独立对抗性复核 H-1）。
    /// 内核层保持"续推动作由触发管线 / 提交管线产出"的既有契约（与理发师同族）。
    /// </summary>
    [Fact]
    public void TriggerSourcedDecisionResolution_LeavesContinuationToCommitPipeline()
    {
        var started = Start(NightPlan(Trigger("sage"), StepFixture.Beat("dawn")));
        var raised = StepMachine.Apply(started.State, new DecisionPointRaisedEvent
        {
            SlotId = null,
            TriggerAbility = new AbilityId("sweetheart"),
            AttributionSeat = new SeatId(3),
            DecisionPoint = new DecisionPoint
            {
                Id = new DecisionPointId("sweetheart:3"),
                Prompt = StepFixture.Prompt("seat:4"),
            },
        })!;

        var elapsed = StepMachine.Handle(raised, new SlotQuotaElapsedInput()).State;
        Assert.Equal(SlotQuotaState.Elapsed, elapsed.Quota);

        var outcome = StepMachine.Handle(elapsed, new ResolveDecisionPointInput
        {
            DecisionPointId = new DecisionPointId("sweetheart:3"),
            Decision = "seat:4",
            Note = "测试：结清",
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.DoesNotContain(
            outcome.Events,
            gameEvent => gameEvent is SlotEnteredEvent or SlotAdvancedEvent or SlotForceAdvancedEvent);
        Assert.Null(outcome.State.AwaitingDecision);
        Assert.Equal(SlotQuotaState.Elapsed, outcome.State.Quota);
    }

    /// <summary>
    /// 触发型裁定与未了结请求并存时，结清裁定**不动**请求：挂起请求不能被顺手清掉
    /// （玩家的选择会无声消失）。
    /// </summary>
    [Fact]
    public void TriggerSourcedDecisionResolution_WithPendingRequest_KeepsRequest()
    {
        var started = Start(NightPlan(Trigger("sage"), StepFixture.Beat("dawn")));
        var withRequest = StepMachine.Apply(started.State, new OperationRequestIssuedEvent
        {
            Request = new OperationRequest
            {
                Id = new OperationRequestId("klutz:1"),
                Addressee = new SeatId(1),
                Origin = OperationRequestOrigin.ForTrigger(new AbilityId("klutz.choice"), "测试：呆瓜死亡选择"),
                Prompt = StepFixture.Prompt("seat:2"),
                Dependencies = [],
            },
        });
        var raised = StepMachine.Apply(withRequest, new DecisionPointRaisedEvent
        {
            SlotId = null,
            TriggerAbility = new AbilityId("sweetheart"),
            AttributionSeat = new SeatId(3),
            DecisionPoint = new DecisionPoint
            {
                Id = new DecisionPointId("sweetheart:3"),
                Prompt = StepFixture.Prompt("seat:4"),
            },
        })!;

        var outcome = StepMachine.Handle(raised, new ResolveDecisionPointInput
        {
            DecisionPointId = new DecisionPointId("sweetheart:3"),
            Decision = "seat:4",
            Note = "测试：结清",
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is SlotEnteredEvent);
        Assert.NotNull(outcome.State.PendingRequest);
    }

    private static StepMachineOutcome Start(StepPlan plan) =>
        StepMachine.StartPhase(plan, previous: null, GameState.Empty);

    private static SageNightOpenedEvent Open(int sage) => new()
    {
        Sage = new SeatId(sage),
        Demon = new SeatId(5),
        DemonCharacter = new CharacterId("vortox"),
        Effective = true,
        Note = "测试：贤者被恶魔杀死",
    };

    private static StepPlan NightPlan(params StepSlot[] slots) => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = slots,
    };

    private static StepSlot Trigger(string id) =>
        StepSlot.Trigger(new StepSlotId(id), new CharacterId("sage"));
}
