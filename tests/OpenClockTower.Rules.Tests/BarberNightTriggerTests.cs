using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 理发师死亡触发的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0033）：
/// 作为理发师死亡且未醉酒 / 未中毒才记「今晚理发」；当夜理发师格开「玩家对 / 不交换」请求；
/// 多存活恶魔先说书人裁定；换角只写角色维度并重绑尚未进入的槽位；摇头 / 作废显式收口。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.EventTriggers"/> 取契约——与运行时取的是同一个对象。
/// </remarks>
public sealed class BarberNightTriggerTests
{
    private static readonly SeatId BarberSeat = new(1);
    private static readonly SeatId DemonSeat = new(5);

    private static IEventTrigger Trigger =>
        RoleContracts.EventTriggers.Single(trigger => trigger.Ability.Value == "barber.swap");

    /// <summary>作为理发师死亡且清醒健康 → 立即记「今晚理发」事实（机制 1 / 3）。</summary>
    [Fact]
    public void DeathAsBarber_OpensFact()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(2, "clockmaker"),
            Row(5, "vortox"));
        var machine = Machine(PlanBeforeBarberSlot());

        var produced = Trigger.Evaluate(Context(state, machine, Death(seat: 1)));

        var opened = Assert.IsType<BarberNightOpenedEvent>(Assert.Single(produced));
        Assert.Equal(BarberSeat, opened.Source);
        Assert.Contains("今晚理发", opened.Note, StringComparison.Ordinal);
    }

    /// <summary>放置条件：死亡时醉酒 / 中毒 → 不触发，但留下可归因的跳过记录（不是静默缺失）。</summary>
    [Theory]
    [InlineData(DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(DrunkState.Sober, PoisonState.Poisoned)]
    public void IneffectiveBarberDeath_RecordsSkip(DrunkState drunk, PoisonState poison)
    {
        var state = State(
            Row(1, "barber", LifeState.Dead, drunk, poison),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeBarberSlot()), Death(seat: 1)));

        var skipped = Assert.IsType<BarberNightSkippedEvent>(Assert.Single(produced));
        Assert.Equal(BarberSeat, skipped.Seat);
        Assert.Contains("醉酒 / 中毒", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>「必须作为理发师死亡才触发」：死后才变成理发师不触发（机制 8）。</summary>
    [Fact]
    public void BecomesBarberAfterDeath_DoesNotTrigger()
    {
        var state = State(
            Row(1, "clockmaker"),
            Row(3, "barber", LifeState.Dead),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanBeforeBarberSlot()),
            Death(seat: 3),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(3),
                Character = new CharacterId("barber"),
                PreviousCharacter = new CharacterId("artist"),
                Reason = "测试：死后变成理发师",
            }));

        Assert.Empty(produced);
    }

    /// <summary>
    /// 批里该席位有角色变化、却缺「变化前角色」→ 判不了死亡时是不是理发师：
    /// 不触发，但**显式**记一条跳过（不猜，也不静默；与 R-0033 第 6 条的姿态一致）。
    /// </summary>
    [Fact]
    public void UndeterminedCharacterAtDeath_RecordsSkip()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanBeforeBarberSlot()),
            Death(seat: 1),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("barber"),
                PreviousCharacter = null,
                Reason = "测试：缺少变化前角色",
            }));

        var skipped = Assert.IsType<BarberNightSkippedEvent>(Assert.Single(produced));
        Assert.Equal(BarberSeat, skipped.Seat);
        Assert.Contains("判不了", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>理发师格已经走过才死亡 → 过时不候（当夜不再补开，不顺延到下一夜）。</summary>
    [Fact]
    public void DeathAfterBarberSlot_RecordsExpirySkip()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanBeforeBarberSlot(), slotIndex: 2),
            Death(seat: 1)));

        var skipped = Assert.IsType<BarberNightSkippedEvent>(Assert.Single(produced));
        Assert.Contains("过时不候", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>首夜顺序表上没有理发师格 → 当夜无法交互，显式跳过。</summary>
    [Fact]
    public void DeathOnFirstNight_RecordsExpirySkip()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));
        var firstNight = new StepPlan
        {
            Label = "sv:night-1",
            Phase = GamePhase.FirstNight,
            Slots = [StepSlot.Beat(new StepSlotId("dusk")), StepSlot.Beat(new StepSlotId("dawn"))],
        };

        var produced = Trigger.Evaluate(Context(state, Machine(firstNight), Death(seat: 1)));

        var skipped = Assert.IsType<BarberNightSkippedEvent>(Assert.Single(produced));
        Assert.Contains("首夜", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>白天死亡 → 立即记账（与恶魔的交互等到当夜；白天本身不开请求）。</summary>
    [Fact]
    public void DeathDuringDay_OpensFactWithoutInteraction()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));
        var day = new StepPlan
        {
            Label = "sv:day-1",
            Phase = GamePhase.Day,
            Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
        };

        var produced = Trigger.Evaluate(Context(state, Machine(day), Death(seat: 1)));

        Assert.IsType<BarberNightOpenedEvent>(Assert.Single(produced));
    }

    /// <summary>死亡就发生在理发师格（格子已进入）→ 同一批把交互补开。</summary>
    [Fact]
    public void DeathWhileBarberSlotCurrent_OpensFactAndRequest()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanAtBarberSlot()), Death(seat: 1)));

        Assert.Contains(produced, gameEvent => gameEvent is BarberNightOpenedEvent);
        var issued = Assert.Single(produced.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(DemonSeat, issued.Request.Addressee);
    }

    /// <summary>理发师格进入但事实没挂着 → 只是一个时机，什么都不开。</summary>
    [Fact]
    public void SlotEntryWithoutFact_DoesNothing()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot()),
            SlotEntered()));

        Assert.Empty(produced);
    }

    /// <summary>
    /// 单存活恶魔：直接向它开「玩家对 / 不交换」请求；候选对排除「另一名恶魔」（已死的恶魔也不行），
    /// 可选自己与已死亡玩家。
    /// </summary>
    [Fact]
    public void SlotEntryWithFact_SingleDemon_IssuesSwapRequest()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(4, "fang-gu", LifeState.Dead),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            SlotEntered()));

        var issued = Assert.Single(produced.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(DemonSeat, issued.Request.Addressee);
        Assert.Equal(OperationRequestOriginKind.Slot, issued.Request.Origin.Kind);
        Assert.Equal("barber:sv:night-2:barber:5", issued.Request.Id.Value);
        Assert.Equal(
            ["pair:1+5", "decline"],
            issued.Request.Prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>
    /// 旅行者与非旅行者不能互换角色：**混合玩家对不进候选**，只给「同为旅行者」或「同为非旅行者」的对；
    /// 「不交换」照旧（R-0060 / 百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式：
    /// 「非旅行者角色也不能在游戏过程中变成旅行者角色……对他摇头示意让他们重新进行选择」）。
    /// </summary>
    [Fact]
    public void SlotEntryWithFact_TravellerInPlay_OmitsMixedPairs()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(2, "butcher"),
            Row(3, "clockmaker"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            SlotEntered()));

        var issued = Assert.Single(produced.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(
            ["pair:1+3", "pair:1+5", "pair:3+5", "decline"],
            issued.Request.Prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>
    /// 也别收得太狠：两名旅行者互换角色**不产生**旅行者 ↔ 非旅行者的转变，照旧可选
    /// （R-0060 只封"跨越旅行者这条线"的对）。
    /// </summary>
    [Fact]
    public void SlotEntryWithFact_TwoTravellers_StillSwappableWithEachOther()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(2, "butcher"),
            Row(3, "clockmaker"),
            Row(4, "deviant"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            SlotEntered()));

        var issued = Assert.Single(produced.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(
            ["pair:1+3", "pair:1+5", "pair:2+4", "pair:3+5", "decline"],
            issued.Request.Prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>答案里跨了旅行者这条线 → 整条命令失败（可重选），不写状态。</summary>
    [Fact]
    public void AnswerMixedTravellerPair_Throws()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(2, "butcher"),
            Row(5, "vortox"));
        var answer = new OperationRequestAnswer { OptionValue = "pair:2+5", Source = ResponseSource.Player };

        Assert.Throws<InvalidOperationException>(() => Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            new OperationRequestAnsweredEvent { RequestId = RequestId(), Answer = answer })));
    }

    /// <summary>两名以上存活恶魔：先说书人裁定用哪名恶魔，再向它开请求（机制 4）。</summary>
    [Fact]
    public void SlotEntryWithFact_MultipleDemons_RaisesDecision_ThenIssuesRequest()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(4, "fang-gu"),
            Row(5, "vortox"));
        var machine = Machine(PlanAtBarberSlot(), barberNight: Fact());

        var raised = Assert.IsType<DecisionPointRaisedEvent>(Assert.Single(Trigger.Evaluate(Context(
            state,
            machine,
            SlotEntered()))));

        // 触发格没有行动者：归属 = 死亡时点以理发师身份落账的席位。
        Assert.Equal(BarberSeat, raised.AttributionSeat);
        Assert.Equal(
            ["seat:4", "seat:5"],
            raised.DecisionPoint.Prompt.Options.Select(option => option.Value).ToArray());

        var resolved = Trigger.Evaluate(Context(
            state,
            machine,
            new DecisionPointResolvedEvent
            {
                DecisionPointId = raised.DecisionPoint.Id,
                Decision = "seat:5",
                Note = "测试：说书人选择 5 号恶魔",
            }));

        var issued = Assert.Single(resolved.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(DemonSeat, issued.Request.Addressee);
    }

    /// <summary>说书人没裁定（强推 / 收口）→ 今晚不交换，事实显式关闭。</summary>
    [Fact]
    public void DemonChoiceNotResolved_ClosesFact()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(4, "fang-gu"),
            Row(5, "vortox"));
        var machine = Machine(PlanAtBarberSlot(), barberNight: Fact());

        var produced = Trigger.Evaluate(Context(
            state,
            machine,
            new DecisionPointResolvedEvent
            {
                DecisionPointId = new DecisionPointId("barber:sv:night-2:barber:demon"),
                Decision = null,
                Note = "测试：强推越过",
            }));

        var closed = Assert.IsType<BarberNightClosedEvent>(Assert.Single(produced));
        Assert.Contains("没有裁定", closed.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// 玩家对 → 两条角色变化（只写角色维度、阵营不变、带变化前角色）+ 尚未进入的槽位重绑 + 收口。
    /// 这里刻意把筑梦师槽位放在理发师格**紧邻的下一格**：真实流程里触发格应答后重进本格
    /// （<see cref="StepMachine.HandleResponse"/> 的触发格分支），触发管线读到的当前槽位仍是理发师格，
    /// 因此紧邻的下一格同样按「尚未进入」重绑。
    /// </summary>
    [Fact]
    public void AnswerPair_SwapsCharactersOnly_AndRebindsPendingSlot()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(3, "dreamer"),
            Row(5, "vortox"));
        var plan = new StepPlan
        {
            Label = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Slots =
            [
                TriggerSlot(),
                StepSlot.Action(
                    new StepSlotId("dreamer"),
                    new SeatId(3),
                    Prompt(),
                    dependencies: null,
                    owner: new CharacterId("dreamer")),
            ],
        };
        var answer = new OperationRequestAnswer { OptionValue = "pair:3+5", Source = ResponseSource.Player };

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(plan, barberNight: Fact()),
            new OperationRequestAnsweredEvent { RequestId = RequestId(), Answer = answer }));

        var changes = produced.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, changes.Length);
        var dreamer = changes.Single(change => change.Seat == new SeatId(3));
        Assert.Equal(new CharacterId("vortox"), dreamer.Character);
        Assert.Equal(new CharacterId("dreamer"), dreamer.PreviousCharacter);
        Assert.Null(dreamer.Alignment);
        Assert.Equal(BarberSeat, dreamer.CausedBy);
        var demon = changes.Single(change => change.Seat == DemonSeat);
        Assert.Equal(new CharacterId("dreamer"), demon.Character);
        Assert.Equal(new CharacterId("vortox"), demon.PreviousCharacter);

        var rebound = Assert.Single(produced.OfType<SlotActivatedEvent>());
        Assert.Equal(1, rebound.SlotIndex);
        Assert.Equal(DemonSeat, rebound.Actor);

        Assert.Contains(produced, gameEvent => gameEvent is BarberNightClosedEvent);
    }

    /// <summary>恶魔摇头 = 不交换：无事发生，事实显式关闭。</summary>
    [Fact]
    public void AnswerDecline_ClosesWithoutChanges()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(3, "dreamer"), Row(5, "vortox"));
        var answer = new OperationRequestAnswer { OptionValue = "decline", Source = ResponseSource.Player };

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            new OperationRequestAnsweredEvent { RequestId = RequestId(), Answer = answer }));

        var closed = Assert.IsType<BarberNightClosedEvent>(Assert.Single(produced));
        Assert.Contains("不交换", closed.Note, StringComparison.Ordinal);
        Assert.Empty(produced.OfType<SeatStateChangedEvent>());
    }

    /// <summary>答案里出现「另一名恶魔」→ 整条命令失败（可重选），不写状态。</summary>
    [Fact]
    public void AnswerPairWithOtherDemon_Throws()
    {
        var state = State(
            Row(1, "barber", LifeState.Dead),
            Row(4, "fang-gu"),
            Row(5, "vortox"));
        var answer = new OperationRequestAnswer { OptionValue = "pair:4+5", Source = ResponseSource.Player };

        Assert.Throws<InvalidOperationException>(() => Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            new OperationRequestAnsweredEvent { RequestId = RequestId(), Answer = answer })));
    }

    /// <summary>请求被作废（强推 / 依赖失效）→ 今晚不交换，事实显式关闭。</summary>
    [Fact]
    public void VoidedRequest_ClosesWithNote()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));
        var voided = new OperationRequestVoid
        {
            Reason = OperationRequestVoidReason.StorytellerForce,
            Note = "测试：说书人强推越过",
        };

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtBarberSlot(), barberNight: Fact()),
            new OperationRequestVoidedEvent { RequestId = RequestId(), Void = voided }));

        var closed = Assert.IsType<BarberNightClosedEvent>(Assert.Single(produced));
        Assert.Contains("作废", closed.Note, StringComparison.Ordinal);
    }

    /// <summary>请求还挂着 → 同一夜不再重复开（幂等）。</summary>
    [Fact]
    public void SlotEntry_WithPendingRequest_DoesNotOpenAgain()
    {
        var state = State(Row(1, "barber", LifeState.Dead), Row(5, "vortox"));
        var machine = Machine(PlanAtBarberSlot(), barberNight: Fact()) with
        {
            PendingRequest = new OperationRequest
            {
                Id = RequestId(),
                Addressee = DemonSeat,
                Origin = OperationRequestOrigin.ForSlot(new StepSlotId("barber"), "sv:night-2", 0),
                Prompt = Prompt(),
            },
        };

        var produced = Trigger.Evaluate(Context(state, machine, SlotEntered()));

        Assert.Empty(produced);
    }

    private static OperationRequestId RequestId() => new("barber:sv:night-2:barber:5");

    private static SeatStateChangedEvent Death(int seat) => new()
    {
        Seat = new SeatId(seat),
        Life = LifeState.Dead,
        Reason = "测试：死亡",
    };

    private static SlotEnteredEvent SlotEntered() => new()
    {
        SlotIndex = 0,
        SlotId = new StepSlotId("barber"),
    };

    private static StepSlot TriggerSlot() =>
        StepSlot.Trigger(new StepSlotId("barber"), new CharacterId("barber"));

    private static ChoicePrompt Prompt() => new()
    {
        Context = "测试用选择",
        Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
        OnNoOption = NoOptionBehavior.BlockAndAlert,
    };

    private static BarberNight Fact() => new()
    {
        Source = BarberSeat,
        Note = "测试：理发师死亡",
    };

    /// <summary>理发师格是当前槽位：黄昏 → 理发师触发格 → 黎明。</summary>
    private static StepPlan PlanAtBarberSlot() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = [TriggerSlot(), StepSlot.DawnWait(new StepSlotId("dawn"))],
    };

    /// <summary>理发师格下标 1（还没走到）：黄昏 → 理发师触发格 → 黎明。</summary>
    private static StepPlan PlanBeforeBarberSlot() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            TriggerSlot(),
            StepSlot.DawnWait(new StepSlotId("dawn")),
        ],
    };

    private static StepMachineState Machine(
        StepPlan plan,
        int slotIndex = 0,
        BarberNight? barberNight = null) =>
        new()
        {
            Plan = plan,
            SlotIndex = slotIndex,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            BarberNight = barberNight,
        };

    private static EventTriggerContext Context(
        GameState state,
        StepMachineState machine,
        params GameEvent[] events) =>
        new()
        {
            State = state,
            Seats = [.. state.Seats.Select(entry => entry.Seat).OrderBy(seat => seat.Value)],
            Events = events,
            Machine = machine,
        };

    private static (int Seat, string Character, LifeState Life, DrunkState Drunk, PoisonState Poison) Row(
        int seat,
        string character,
        LifeState life = LifeState.Alive,
        DrunkState drunk = DrunkState.Sober,
        PoisonState poison = PoisonState.Healthy) =>
        (seat, character, life, drunk, poison);

    private static GameState State(
        params (int Seat, string Character, LifeState Life, DrunkState Drunk, PoisonState Poison)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(row.Character is "vortox" or "fang-gu" ? Alignment.Evil : Alignment.Good),
                    Life = Fact(row.Life),
                    Drunk = Fact(row.Drunk),
                    Poison = Fact(row.Poison),
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
