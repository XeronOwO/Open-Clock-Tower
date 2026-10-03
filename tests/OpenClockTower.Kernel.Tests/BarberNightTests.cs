using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 「今晚理发」事实的机器级生命周期（平台口径见 <c>docs/standard/rulings.md</c> R-0033）：
/// 死亡立即记账、跨白天 → 夜晚保留、夜晚走完仍未消费时显式「过时不候」；
/// 以及触发格的进入语义与「裁定点按归属收口」的内核修正。
/// </summary>
public sealed class BarberNightTests
{
    /// <summary>白天记的事实跨阶段保留到当夜；消费后关闭。</summary>
    [Fact]
    public void Fact_CarriesAcrossDayIntoNight_AndClosesOnConsumption()
    {
        var day = StepMachine.StartDay(DayPlan(), dayNumber: 1);
        var opened = StepMachine.Apply(
            day.State,
            new BarberNightOpenedEvent { Source = new SeatId(1), Note = "白天处决理发师" });

        Assert.NotNull(opened);
        Assert.NotNull(opened!.BarberNight);
        Assert.Equal(new SeatId(1), opened.BarberNight!.Source);

        var night = StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk"), Trigger("barber"), StepFixture.DawnWait("dawn")),
            opened,
            GameState.Empty);
        Assert.NotNull(night.State.BarberNight);

        var closed = StepMachine.Apply(night.State, new BarberNightClosedEvent { Note = "恶魔执行了交换" });
        Assert.Null(closed!.BarberNight);
    }

    /// <summary>同一夜不能开两次事实；没有开启也不能关闭（事件流损坏一律显式失败）。</summary>
    [Fact]
    public void Fact_CannotOpenTwiceOrCloseWithoutOpen()
    {
        var started = StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk"), Trigger("barber")),
            previous: null,
            GameState.Empty);

        var opened = StepMachine.Apply(
            started.State,
            new BarberNightOpenedEvent { Source = new SeatId(1), Note = "理发师死亡" });

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            opened,
            new BarberNightOpenedEvent { Source = new SeatId(1), Note = "重复开启" }));

        var closed = StepMachine.Apply(opened, new BarberNightClosedEvent { Note = "交换完成" });
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            closed,
            new BarberNightClosedEvent { Note = "重复关闭" }));
    }

    /// <summary>夜晚走完仍未消费：显式记「过时不候」再清空，关闭事件排在阶段完成之前。</summary>
    [Fact]
    public void UnconsumedFact_IsClosedAtNightEnd_WithExpiryNote()
    {
        var started = StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk"), Trigger("barber"), StepFixture.Beat("dawn")),
            previous: null,
            GameState.Empty);
        var state = StepMachine.Apply(
            started.State,
            new BarberNightOpenedEvent { Source = new SeatId(1), Note = "理发师死亡" })!;

        var lastEvents = new List<GameEvent>();
        for (var attempt = 0; attempt < 4 && !state.IsPlanCompleted; attempt++)
        {
            var outcome = StepMachine.Handle(state, new SlotQuotaElapsedInput());
            Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
            lastEvents = [.. outcome.Events];
            state = outcome.State;
        }

        Assert.True(state.IsPlanCompleted);
        Assert.Null(state.BarberNight);

        var closed = Assert.Single(lastEvents.OfType<BarberNightClosedEvent>());
        Assert.Contains("过时不候", closed.Note, StringComparison.Ordinal);
        var closeIndex = lastEvents.FindIndex(gameEvent => gameEvent is BarberNightClosedEvent);
        var completedIndex = lastEvents.FindIndex(gameEvent => gameEvent is PhaseCompletedEvent);
        Assert.True(completedIndex > closeIndex, "「过时不候」收口必须排在阶段完成之前");
    }

    /// <summary>
    /// 触发格：进入时只记「时间到了」——持有者存活、这一格也没有请求，
    /// 更不许把「在表上没有行动契约」误判成空槽位阻塞（理发师本人在场也不行动）。
    /// </summary>
    [Fact]
    public void TriggerSlot_EntersWithoutBlockOrRequest()
    {
        var plan = NightPlan(StepFixture.Beat("dusk"), Trigger("barber"));
        var ledger = Ledger((1, "barber", LifeState.Alive));

        var started = StepMachine.StartPhase(plan, previous: null, ledger);
        var advanced = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());

        Assert.Equal(1, advanced.State.SlotIndex);
        Assert.Equal(StepSlotKind.Trigger, advanced.State.CurrentSlot!.Kind);
        Assert.Contains(
            advanced.Events,
            gameEvent => gameEvent is SlotEnteredEvent entered && entered.SlotId == new StepSlotId("barber"));
        Assert.DoesNotContain(
            advanced.Events,
            gameEvent => gameEvent is SlotBlockedEvent or PromptSkippedEvent or OperationRequestIssuedEvent);
    }

    /// <summary>
    /// 裁定点按**归属**收口：触发器开出的裁定点（标识不属于当前槽位）只落裁定本身，
    /// 不驱动槽位结算、也不自动推进——续推动作由同一批的触发管线产出。
    /// </summary>
    [Fact]
    public void ForeignDecision_ResolvesWithoutAdvancingOrSettling()
    {
        var started = StepMachine.StartPhase(NightPlan(Trigger("barber")), previous: null, GameState.Empty);
        var decisionId = new DecisionPointId("barber:test-night-2:barber:demon");
        var withDecision = StepMachine.Apply(
            started.State,
            new DecisionPointRaisedEvent
            {
                SlotId = new StepSlotId("barber"),
                AttributionSeat = new SeatId(1),
                DecisionPoint = new DecisionPoint
                {
                    Id = decisionId,
                    Prompt = StepFixture.Prompt("seat:1", "seat:2"),
                },
            })!;

        Assert.Equal(new SeatId(1), withDecision.AwaitingDecisionSeat);

        var outcome = StepMachine.Handle(
            withDecision,
            new ResolveDecisionPointInput
            {
                DecisionPointId = decisionId,
                Decision = "seat:1",
                Note = "测试：触发器裁定点",
            });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Single(outcome.Events.OfType<DecisionPointResolvedEvent>());
        Assert.DoesNotContain(
            outcome.Events,
            gameEvent => gameEvent is SlotAdvancedEvent or SlotForceAdvancedEvent or SlotEnteredEvent);
        Assert.Equal(0, outcome.State.SlotIndex);
        Assert.Null(outcome.State.AwaitingDecision);
        Assert.Null(outcome.State.AwaitingDecisionSeat);
    }

    /// <summary>
    /// 触发格的槽位来源请求答完后**重进本格**（节拍重新起算）、不自动推进：
    /// 触发管线在同一批的批末先完成换角重绑与收口；若在这里推进，管线读到的当前下标已经越过这一格，
    /// 紧邻的下一格会被当成「已进入」而漏掉重绑（R-0032），且结果会随"答在配额前 / 后"抖动。
    /// </summary>
    [Fact]
    public void TriggerSlotAnswer_ReentersTheSlot_WithoutAdvancing()
    {
        var started = StepMachine.StartPhase(
            NightPlan(Trigger("barber"), StepFixture.Beat("dawn")),
            previous: null,
            GameState.Empty);
        var request = new OperationRequest
        {
            Id = new OperationRequestId("barber:sv:night-2:barber:5"),
            Addressee = new SeatId(5),
            Origin = OperationRequestOrigin.ForSlot(new StepSlotId("barber"), "sv:night-2", 0),
            Prompt = StepFixture.Prompt("seat:1"),
        };
        var pending = StepMachine.Apply(
            started.State,
            new OperationRequestIssuedEvent { Request = request })!;

        var outcome = StepMachine.Handle(
            pending,
            new SubmitResponseInput
            {
                RequestId = request.Id,
                OptionValue = "seat:1",
                Source = ResponseSource.Player,
            });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(
            outcome.Events,
            gameEvent => gameEvent is SlotEnteredEvent
                && ((SlotEnteredEvent)gameEvent).SlotIndex == 0
                && ((SlotEnteredEvent)gameEvent).SlotId == new StepSlotId("barber"));
        Assert.DoesNotContain(
            outcome.Events,
            gameEvent => gameEvent is SlotAdvancedEvent or SlotForceAdvancedEvent or PhaseCompletedEvent);
        Assert.Equal(0, outcome.State.SlotIndex);
        Assert.Equal(SlotQuotaState.Running, outcome.State.Quota);
        Assert.Null(outcome.State.PendingRequest);
    }

    /// <summary>事实不允许从夜晚原样带进新阶段（夜末收口缺失必须显式失败，不静默顺延）。</summary>
    [Fact]
    public void Fact_CannotCarryOutOfUnfinishedNight()
    {
        var night = StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk"), StepFixture.Beat("dawn")),
            previous: null,
            GameState.Empty);
        var withFact = StepMachine.Apply(
            night.State,
            new BarberNightOpenedEvent { Source = new SeatId(1), Note = "理发师死亡" });

        Assert.Throws<InvalidOperationException>(() => StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk")),
            withFact,
            GameState.Empty));
    }

    private static StepPlan DayPlan() => new()
    {
        Label = "sv:day-1",
        Phase = GamePhase.Day,
        Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
    };

    private static StepPlan NightPlan(params StepSlot[] slots) => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = slots,
    };

    private static StepSlot Trigger(string id) =>
        StepSlot.Trigger(new StepSlotId(id), new CharacterId("barber"));

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Life = Fact(row.Life),
                    Alignment = Fact(Alignment.Good),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
