using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 呆瓜死亡选择的触发器回归（R-0027）：公告时点开请求、能力不生效可归因跳过、答题 / 作废收口与幂等。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.EventTriggers"/> 取契约——与运行时取的是同一个对象。
/// </remarks>
public sealed class KlutzChoiceTriggerTests
{
    private static IEventTrigger Trigger =>
        RoleContracts.EventTriggers.Single(trigger => trigger.Ability.Value == "klutz.choice");

    /// <summary>夜间死亡的呆瓜在黎明被公告 → 开出一条触发来源的请求，候选只含存活席位。</summary>
    [Fact]
    public void DawnAnnouncement_OpensTriggerRequest_ForLivingCandidates()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Sober),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober),
            (3, "no-dashii", LifeState.Dead, DrunkState.Sober));

        var produced = Trigger.Evaluate(Context(state, events: [new DayStartedEvent { DayNumber = 1 }]));

        var issued = Assert.IsType<OperationRequestIssuedEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(1), issued.Request.Addressee);
        Assert.Equal(OperationRequestOriginKind.Trigger, issued.Request.Origin.Kind);
        Assert.Equal(new AbilityId("klutz.choice"), issued.Request.Origin.TriggerAbility!.Value);
        Assert.Equal(["seat:2"], issued.Request.Prompt.Options.Select(option => option.Value));
    }

    /// <summary>白天死亡随提交即时公告（R-0022 第 2 条）→ 立即开选择；夜晚（黎明之前）不开。</summary>
    [Fact]
    public void DayDeath_OpensImmediately_NightDeathWaitsForDawn()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Sober),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober));
        var death = new SeatStateChangedEvent
        {
            Seat = new SeatId(1),
            Life = LifeState.Dead,
            Reason = "测试：死亡",
        };

        var day = Trigger.Evaluate(Context(state, dayWasOpen: true, events: [death]));
        Assert.IsType<OperationRequestIssuedEvent>(Assert.Single(day));

        var night = Trigger.Evaluate(Context(state, dayWasOpen: false, events: [death]));
        Assert.Empty(night);
    }

    /// <summary>死亡时能力未生效（醉酒 / 中毒）→ 不开选择，但留下可归因的跳过记录（不是静默缺失）。</summary>
    [Fact]
    public void DrunkKlutz_SkipsWithReason()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Drunk),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober));

        var produced = Trigger.Evaluate(Context(state, events: [new DayStartedEvent { DayNumber = 1 }]));

        var skipped = Assert.IsType<KlutzChoiceSkippedEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(1), skipped.Klutz);
        Assert.Contains("未生效", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>已经有选择记录（含跳过）→ 后续黎明不再重复开选择（幂等）。</summary>
    [Fact]
    public void RecordedChoice_IsIdempotent()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Sober),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober));
        var machine = Machine(new KlutzChoiceRecord
        {
            Klutz = new SeatId(1),
            Target = new SeatId(2),
            Detail = "测试：已经选过",
        });

        var produced = Trigger.Evaluate(Context(
            state,
            machine: machine,
            events: [new DayStartedEvent { DayNumber = 3 }]));

        Assert.Empty(produced);
    }

    /// <summary>玩家答完 → 产出呆瓜选择的领域事实（胜负由 OutcomeEvaluator 在同一批求值）。</summary>
    [Fact]
    public void AnsweredRequest_ProducesChoiceMadeEvent()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Sober),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober));
        var request = OpenRequest(state);
        var answer = new OperationRequestAnswer
        {
            OptionValue = "seat:2",
            Source = ResponseSource.Player,
        };
        var machine = Machine() with
        {
            PendingRequest = request with { Status = OperationRequestStatus.Answered, Answer = answer },
        };

        var produced = Trigger.Evaluate(Context(
            state,
            machine: machine,
            events: [new OperationRequestAnsweredEvent { RequestId = request.Id, Answer = answer }]));

        var choice = Assert.IsType<KlutzChoiceMadeEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(1), choice.Klutz);
        Assert.Equal(new SeatId(2), choice.Target);
    }

    /// <summary>强制作废 = 这次没有做出选择：写一条可归因的跳过，后续黎明不再重开。</summary>
    [Fact]
    public void VoidedRequest_ProducesSkippedRecord()
    {
        var state = State(
            (1, "klutz", LifeState.Dead, DrunkState.Sober),
            (2, "clockmaker", LifeState.Alive, DrunkState.Sober));
        var request = OpenRequest(state);
        var voided = new OperationRequestVoid
        {
            Reason = OperationRequestVoidReason.StorytellerForce,
            Note = "测试：说书人作废",
        };
        var machine = Machine() with
        {
            PendingRequest = request with { Status = OperationRequestStatus.Voided, Voided = voided },
        };

        var produced = Trigger.Evaluate(Context(
            state,
            machine: machine,
            events: [new OperationRequestVoidedEvent { RequestId = request.Id, Void = voided }]));

        var skipped = Assert.IsType<KlutzChoiceSkippedEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(1), skipped.Klutz);
        Assert.Contains("作废", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 请求已经随槽位推进被清空（说书人强推越过白天）→ 仍按**请求标识**认领并写跳过记录：
    /// 否则下一个黎明会重复开选择（独立对抗性复核 F-1）。
    /// </summary>
    [Fact]
    public void VoidedRequest_AfterSlotAdvance_StillRecordsTheSkip()
    {
        var state = State(
            (1, "clockmaker", LifeState.Alive, DrunkState.Sober),
            (2, "klutz", LifeState.Dead, DrunkState.Sober));
        var requestId = new OperationRequestId("klutz:2");
        var voided = new OperationRequestVoid
        {
            Reason = OperationRequestVoidReason.StorytellerForce,
            Note = "测试：说书人强推越过白天",
        };

        // 机器里没有挂起请求（已被槽位推进清空），只有那条作废事件。
        var produced = Trigger.Evaluate(Context(
            state,
            machine: Machine(),
            events: [new OperationRequestVoidedEvent { RequestId = requestId, Void = voided }]));

        var skipped = Assert.IsType<KlutzChoiceSkippedEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(2), skipped.Klutz);
        Assert.Contains("作废", skipped.Reason, StringComparison.Ordinal);
    }

    private static OperationRequest OpenRequest(GameState state)
    {
        var produced = Trigger.Evaluate(Context(state, events: [new DayStartedEvent { DayNumber = 1 }]));
        return Assert.IsType<OperationRequestIssuedEvent>(Assert.Single(produced)).Request;
    }

    private static EventTriggerContext Context(
        GameState state,
        StepMachineState? machine = null,
        bool dayWasOpen = false,
        params GameEvent[] events) =>
        new()
        {
            State = state,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            Events = events,
            Machine = machine ?? Machine(),
            DayWasOpen = dayWasOpen,
        };

    private static StepMachineState Machine(params KlutzChoiceRecord[] choices) =>
        new()
        {
            Plan = new StepPlan
            {
                Label = "test:day-1",
                Phase = GamePhase.Day,
                Slots = [StepSlot.DayWindow(new StepSlotId("day"))],
            },
            SlotIndex = 0,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            KlutzChoices = choices,
        };

    private static GameState State(
        params (int Seat, string Character, LifeState Life, DrunkState Drunk)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(row.Character == "no-dashii" ? Alignment.Evil : Alignment.Good),
                    Life = Fact(row.Life),
                    Drunk = Fact(row.Drunk),
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
