using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 贤者死亡触发的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0038）：
/// 作为贤者**被恶魔杀死**才记「当晚展示」事实；处决 / 女巫诅咒 / 麻脸巫婆追加死亡不触发；
/// 当夜贤者格开「两名玩家」裁定，结清后信息只到贤者本人；未生效与涡流标「可能为假」。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.EventTriggers"/> 取契约——与运行时取的是同一个对象。
/// </remarks>
public sealed class SageNightTriggerTests
{
    private static readonly SeatId SageSeat = new(2);
    private static readonly SeatId DemonSeat = new(5);

    private static IEventTrigger Trigger =>
        RoleContracts.EventTriggers.Single(trigger => trigger.Ability.Value == "sage");

    /// <summary>作为贤者被恶魔杀死 → 立即记「当晚展示」事实（含击杀者与生效判定）。</summary>
    [Fact]
    public void DemonKill_OpensFact()
    {
        var state = State(
            Row(2, "sage", LifeState.Dead),
            Row(3, "clockmaker"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2, causedBy: 5)));

        var opened = Assert.IsType<SageNightOpenedEvent>(Assert.Single(produced));
        Assert.Equal(SageSeat, opened.Sage);
        Assert.Equal(DemonSeat, opened.Demon);
        Assert.Equal(new CharacterId("vortox"), opened.DemonCharacter);
        Assert.True(opened.Effective);
        Assert.Contains("当晚展示", opened.Note, StringComparison.Ordinal);
    }

    /// <summary>死亡时醉酒 / 中毒 → 能力未生效，但**照常开事实**（信息仍由说书人给，标可能为假）。</summary>
    [Theory]
    [InlineData(DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(DrunkState.Sober, PoisonState.Poisoned)]
    public void IneffectiveSage_OpensFactWithEffectiveFalse(DrunkState drunk, PoisonState poison)
    {
        var state = State(
            Row(2, "sage", LifeState.Dead, drunk, poison),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2, causedBy: 5)));

        var opened = Assert.IsType<SageNightOpenedEvent>(Assert.Single(produced));
        Assert.False(opened.Effective);
    }

    /// <summary>死于处决（死亡事件没有击杀者归因）→ 不触发，留可归因的跳过。</summary>
    [Fact]
    public void Execution_RecordsSkip()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2)));

        var skipped = Assert.IsType<SageNightSkippedEvent>(Assert.Single(produced));
        Assert.Equal(SageSeat, skipped.Sage);
        Assert.Contains("非恶魔来源", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>击杀者不是恶魔（女巫诅咒 / 处罚处决 / 麻脸巫婆追加死亡同族）→ 不触发。</summary>
    [Fact]
    public void MinionKill_RecordsSkip()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(4, "witch"), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2, causedBy: 4)));

        var skipped = Assert.IsType<SageNightSkippedEvent>(Assert.Single(produced));
        Assert.Contains("不是恶魔", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>击杀者角色未观测 → 判不了，不猜也不触发。</summary>
    [Fact]
    public void UnknownKillerCharacter_RecordsSkip()
    {
        var state = State(Row(2, "sage", LifeState.Dead));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2, causedBy: 5)));

        var skipped = Assert.IsType<SageNightSkippedEvent>(Assert.Single(produced));
        Assert.Contains("未观测", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>不是作为贤者死亡 → 与本触发器无关（不记录跳过）。</summary>
    [Fact]
    public void NotSageDeath_DoesNothing()
    {
        var state = State(Row(2, "clockmaker", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanBeforeSageSlot()), Death(2, causedBy: 5)));

        Assert.Empty(produced);
    }

    /// <summary>贤者格已经走过才死亡 → 过时不候（当夜不再补开，不顺延到下一夜）。</summary>
    [Fact]
    public void DeathAfterSageSlot_RecordsExpirySkip()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanBeforeSageSlot(), slotIndex: 2),
            Death(2, causedBy: 5)));

        var skipped = Assert.IsType<SageNightSkippedEvent>(Assert.Single(produced));
        Assert.Contains("过时不候", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>贤者格进入但事实没挂着 → 只是一个时机，什么都不开。</summary>
    [Fact]
    public void SlotEntryWithoutFact_DoesNothing()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(PlanAtSageSlot()), SlotEntered()));

        Assert.Empty(produced);
    }

    /// <summary>事实挂着、贤者格进入 → 开「两名玩家」原子选择（候选 = 除贤者外的两两组合）。</summary>
    [Fact]
    public void SlotEntryWithFact_RaisesPairDecision()
    {
        var state = State(
            Row(2, "sage", LifeState.Dead),
            Row(3, "clockmaker"),
            Row(4, "witch"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            SlotEntered()));

        var raised = Assert.IsType<DecisionPointRaisedEvent>(Assert.Single(produced));
        Assert.Equal(new StepSlotId("sage"), raised.SlotId);
        Assert.Null(raised.TriggerAbility);

        // 触发格没有行动者：归属 = 死亡时点以贤者身份落账的席位。
        Assert.Equal(SageSeat, raised.AttributionSeat);
        Assert.Equal(new DecisionPointId("sage:sv:night-2:sage:pair"), raised.DecisionPoint.Id);
        Assert.Equal(
            ["pair:3+4", "pair:3+5", "pair:4+5"],
            raised.DecisionPoint.Prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>裁定结清 → 信息结果只发给贤者本人 + 事实关闭；注记里带推演的击杀者。</summary>
    [Fact]
    public void Resolve_BuildsInformationResult_OnlyForSage()
    {
        var state = State(
            Row(2, "sage", LifeState.Dead),
            Row(3, "clockmaker"),
            Row(4, "witch"),
            Row(5, "no-dashii"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            Resolved("pair:3+5")));

        var info = Assert.Single(produced.OfType<InformationResultIssuedEvent>());
        Assert.Equal(SageSeat, info.Recipient);
        Assert.Contains("3 号", info.Content, StringComparison.Ordinal);
        Assert.Contains("5 号", info.Content, StringComparison.Ordinal);
        Assert.False(info.MayBeFalse);
        Assert.Contains("5 号", info.Note, StringComparison.Ordinal);
        Assert.Single(produced.OfType<SageNightClosedEvent>());
    }

    /// <summary>能力未生效 → 信息照发、标「可能为假」，注记说明死亡时状态。</summary>
    [Fact]
    public void Resolve_IneffectiveSage_MarksMayBeFalse()
    {
        var state = State(
            Row(2, "sage", LifeState.Dead, DrunkState.Drunk, PoisonState.Healthy),
            Row(3, "clockmaker"),
            Row(5, "no-dashii"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact(effective: false)),
            Resolved("pair:3+5")));

        var info = Assert.Single(produced.OfType<InformationResultIssuedEvent>());
        Assert.True(info.MayBeFalse);
        Assert.Contains("未生效", info.Note, StringComparison.Ordinal);
    }

    /// <summary>涡流在场 → 信息必须为假（R-0028）：标「可能为假」并带注记。</summary>
    [Fact]
    public void Resolve_WithVortox_MarksMustBeFalse()
    {
        var state = State(
            Row(2, "sage", LifeState.Dead),
            Row(3, "clockmaker"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            Resolved("pair:3+5")));

        var info = Assert.Single(produced.OfType<InformationResultIssuedEvent>());
        Assert.True(info.MayBeFalse);
        Assert.Contains("R-0028", info.Note, StringComparison.Ordinal);
    }

    /// <summary>说书人没有裁定（强推 / 收口）→ 不发信息，事实显式关闭。</summary>
    [Fact]
    public void ResolveWithoutDecision_ClosesFactWithoutInfo()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(3, "clockmaker"), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            new DecisionPointResolvedEvent
            {
                DecisionPointId = new DecisionPointId("sage:sv:night-2:sage:pair"),
                Decision = null,
                Note = "测试：强推越过",
            }));

        Assert.Empty(produced.OfType<InformationResultIssuedEvent>());
        var closed = Assert.IsType<SageNightClosedEvent>(Assert.Single(produced));
        Assert.Contains("没有完成", closed.Note, StringComparison.Ordinal);
    }

    /// <summary>越界的展示组合（席位不在本局）→ 整条命令失败，不写信息。</summary>
    [Fact]
    public void Resolve_UnknownSeat_Throws()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(3, "clockmaker"), Row(5, "vortox"));

        Assert.Throws<InvalidOperationException>(() => Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            Resolved("pair:3+99"))));
    }

    /// <summary>展示组合里出现贤者本人 → 整条命令失败（候选集合之外的编码不采纳）。</summary>
    [Fact]
    public void Resolve_PairIncludingSage_Throws()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(3, "clockmaker"), Row(5, "vortox"));

        Assert.Throws<InvalidOperationException>(() => Trigger.Evaluate(Context(
            state,
            Machine(PlanAtSageSlot(), sageNight: Fact()),
            Resolved("pair:2+3"))));
    }

    /// <summary>裁定点还挂着 → 同一夜不再重复开（幂等）。</summary>
    [Fact]
    public void SlotEntry_WithAwaitingDecision_DoesNotOpenAgain()
    {
        var state = State(Row(2, "sage", LifeState.Dead), Row(3, "clockmaker"), Row(5, "vortox"));
        var machine = Machine(PlanAtSageSlot(), sageNight: Fact()) with
        {
            AwaitingDecision = new DecisionPoint
            {
                Id = new DecisionPointId("sage:sv:night-2:sage:pair"),
                Prompt = Prompt(),
            },
        };

        var produced = Trigger.Evaluate(Context(state, machine, SlotEntered()));

        Assert.Empty(produced);
    }

    private static DecisionPointResolvedEvent Resolved(string pair) => new()
    {
        DecisionPointId = new DecisionPointId("sage:sv:night-2:sage:pair"),
        Decision = pair,
        Note = "测试：说书人完成展示",
    };

    private static SeatStateChangedEvent Death(int seat, int? causedBy = null) => new()
    {
        Seat = new SeatId(seat),
        Life = LifeState.Dead,
        Reason = "测试：死亡",
        CausedBy = causedBy is { } value ? new SeatId(value) : null,
    };

    private static SlotEnteredEvent SlotEntered() => new()
    {
        SlotIndex = 0,
        SlotId = new StepSlotId("sage"),
    };

    private static StepSlot TriggerSlot() =>
        StepSlot.Trigger(new StepSlotId("sage"), new CharacterId("sage"));

    private static ChoicePrompt Prompt() => new()
    {
        Context = "测试用选择",
        Options = [new DecisionOption { Value = "pair:3+5", Preview = "3 号 + 5 号" }],
        OnNoOption = NoOptionBehavior.BlockAndAlert,
    };

    private static SageNight Fact(bool effective = true) => new()
    {
        Sage = SageSeat,
        Demon = DemonSeat,
        DemonCharacter = new CharacterId("no-dashii"),
        Effective = effective,
        Note = "测试：贤者被恶魔杀死",
    };

    /// <summary>贤者格是当前槽位：贤者触发格 → 黎明。</summary>
    private static StepPlan PlanAtSageSlot() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = [TriggerSlot(), StepSlot.DawnWait(new StepSlotId("dawn"))],
    };

    /// <summary>贤者格下标 1（还没走到）：黄昏 → 贤者触发格 → 黎明。</summary>
    private static StepPlan PlanBeforeSageSlot() => new()
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
        SageNight? sageNight = null) =>
        new()
        {
            Plan = plan,
            SlotIndex = slotIndex,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            SageNight = sageNight,
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
