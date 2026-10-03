using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 心上人死亡触发的规则回归（平台口径见 <c>docs/standard/rulings.md</c> R-0039）：
/// 作为心上人死亡（任何死因）时**立即**开触发型说书人裁定；结清后施加持续醉酒效果
/// （来源换角时终止）；未生效 / 未观测 / 未裁定都留显式的跳过记录。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.EventTriggers"/> 取契约——与运行时取的是同一个对象。
/// </remarks>
public sealed class SweetheartDeathTriggerTests
{
    private static readonly SeatId SweetheartSeat = new(1);

    private static IEventTrigger Trigger =>
        RoleContracts.EventTriggers.Single(trigger => trigger.Ability.Value == "sweetheart");

    /// <summary>作为心上人死亡且清醒健康 → 立即开触发型裁定（无槽位来源、候选 = 全体席位）。</summary>
    [Fact]
    public void DeathAsSweetheart_RaisesTriggerDecision()
    {
        var state = State(
            Row(1, "sweetheart", LifeState.Dead),
            Row(2, "clockmaker"),
            Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(), Death(1)));

        var raised = Assert.IsType<DecisionPointRaisedEvent>(Assert.Single(produced));
        Assert.Null(raised.SlotId);
        Assert.Equal(new AbilityId("sweetheart"), raised.TriggerAbility);

        // 触发型裁定没有槽位：归属 = 死亡的心上人本人。
        Assert.Equal(SweetheartSeat, raised.AttributionSeat);
        Assert.Equal(new DecisionPointId("sweetheart:1"), raised.DecisionPoint.Id);
        Assert.Equal(
            ["seat:1", "seat:2", "seat:5"],
            raised.DecisionPoint.Prompt.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>死亡时能力未生效（醉酒 / 中毒）→ 不开裁定，留可归因的跳过。</summary>
    [Theory]
    [InlineData(DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(DrunkState.Sober, PoisonState.Poisoned)]
    public void IneffectiveDeath_RecordsSkip(DrunkState drunk, PoisonState poison)
    {
        var state = State(
            Row(1, "sweetheart", LifeState.Dead, drunk, poison),
            Row(2, "clockmaker"));

        var produced = Trigger.Evaluate(Context(state, Machine(), Death(1)));

        var skipped = Assert.IsType<SweetheartDeathSkippedEvent>(Assert.Single(produced));
        Assert.Equal(SweetheartSeat, skipped.Sweetheart);
        Assert.Contains("未生效", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>死亡时醉酒 / 中毒维度未观测 → 判不了，不猜也不开裁定。</summary>
    [Fact]
    public void UnobservedStatus_RecordsSkip()
    {
        var state = StateWithUnobservedStatus((1, "sweetheart", LifeState.Dead), (2, "clockmaker", LifeState.Alive));

        var produced = Trigger.Evaluate(Context(state, Machine(), Death(1)));

        var skipped = Assert.IsType<SweetheartDeathSkippedEvent>(Assert.Single(produced));
        Assert.Contains("未观测", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>不是作为心上人死亡 → 与本触发器无关。</summary>
    [Fact]
    public void NotSweetheartDeath_DoesNothing()
    {
        var state = State(Row(1, "clockmaker", LifeState.Dead), Row(2, "vortox"));

        var produced = Trigger.Evaluate(Context(state, Machine(), Death(1)));

        Assert.Empty(produced);
    }

    /// <summary>裁定结清 → 施加持续醉酒效果（来源 = 心上人、来源死亡不终止、维度 = 醉酒）。</summary>
    [Fact]
    public void Resolve_AppliesDrunkEffect()
    {
        var state = State(Row(1, "sweetheart", LifeState.Dead), Row(2, "clockmaker"), Row(5, "vortox"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(),
            new DecisionPointResolvedEvent
            {
                DecisionPointId = new DecisionPointId("sweetheart:1"),
                Decision = "seat:5",
                Note = "测试：说书人选择 5 号醉酒",
            }));

        var applied = Assert.IsType<PersistentEffectAppliedEvent>(Assert.Single(produced));
        var effect = applied.Effect;
        Assert.Equal(new EffectId("sweetheart:1:drunk"), effect.Id);
        Assert.Equal(SweetheartSeat, effect.Source);
        Assert.Equal(new SeatId(5), effect.Target);
        Assert.Equal(new CharacterId("sweetheart"), effect.SourceCharacter);
        Assert.Equal(EffectDimension.Drunk, effect.Dimension);
        Assert.True(effect.SourceStateIndependent);
    }

    /// <summary>说书人没有裁定（强推 / 收口）→ 醉酒不施加，留显式跳过。</summary>
    [Fact]
    public void ResolveWithoutDecision_RecordsSkip()
    {
        var state = State(Row(1, "sweetheart", LifeState.Dead), Row(2, "clockmaker"));

        var produced = Trigger.Evaluate(Context(
            state,
            Machine(),
            new DecisionPointResolvedEvent
            {
                DecisionPointId = new DecisionPointId("sweetheart:1"),
                Decision = null,
                Note = "测试：强推越过",
            }));

        var skipped = Assert.IsType<SweetheartDeathSkippedEvent>(Assert.Single(produced));
        Assert.Contains("没有裁定", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>效果已经落账 → 同一名心上人的死亡不再重复处理（幂等）。</summary>
    [Fact]
    public void DeathWithExistingEffect_DoesNothing()
    {
        var state = StateWithEffect(
            Row(1, "sweetheart", LifeState.Dead),
            Row(2, "clockmaker"));

        var produced = Trigger.Evaluate(Context(state, Machine(), Death(1)));

        Assert.Empty(produced);
    }

    /// <summary>裁定点还挂着 → 不再重复开（幂等）。</summary>
    [Fact]
    public void DeathWithPendingDecision_DoesNothing()
    {
        var state = State(Row(1, "sweetheart", LifeState.Dead), Row(2, "clockmaker"));
        var machine = Machine() with
        {
            AwaitingDecision = new DecisionPoint
            {
                Id = new DecisionPointId("sweetheart:1"),
                Prompt = Prompt(),
            },
            AwaitingDecisionTriggerAbility = new AbilityId("sweetheart"),
        };

        var produced = Trigger.Evaluate(Context(state, machine, Death(1)));

        Assert.Empty(produced);
    }

    /// <summary>
    /// 已经有一条未了结的心上人裁定（例：更早的死亡尚未裁定）时，另一条死亡不再开
    /// （步骤机同时只挂一个裁定点）——显式跳过，不静默覆盖。
    /// </summary>
    [Fact]
    public void SecondDeathWhilePending_RecordsSkip()
    {
        var state = State(
            Row(1, "sweetheart", LifeState.Dead),
            Row(3, "sweetheart", LifeState.Dead));
        var machine = Machine() with
        {
            AwaitingDecision = new DecisionPoint
            {
                Id = new DecisionPointId("sweetheart:1"),
                Prompt = Prompt(),
            },
            AwaitingDecisionTriggerAbility = new AbilityId("sweetheart"),
        };

        var produced = Trigger.Evaluate(Context(state, machine, Death(3)));

        var skipped = Assert.IsType<SweetheartDeathSkippedEvent>(Assert.Single(produced));
        Assert.Equal(new SeatId(3), skipped.Sweetheart);
        Assert.Contains("未了结", skipped.Reason, StringComparison.Ordinal);
    }

    /// <summary>越界的醉酒目标（席位不在本局）→ 整条命令失败，不写效果。</summary>
    [Fact]
    public void Resolve_UnknownSeat_Throws()
    {
        var state = State(Row(1, "sweetheart", LifeState.Dead), Row(2, "clockmaker"));

        Assert.Throws<InvalidOperationException>(() => Trigger.Evaluate(Context(
            state,
            Machine(),
            new DecisionPointResolvedEvent
            {
                DecisionPointId = new DecisionPointId("sweetheart:1"),
                Decision = "seat:99",
                Note = "测试：越界目标",
            })));
    }

    private static SeatStateChangedEvent Death(int seat) => new()
    {
        Seat = new SeatId(seat),
        Life = LifeState.Dead,
        Reason = "测试：死亡",
    };

    private static ChoicePrompt Prompt() => new()
    {
        Context = "测试用选择",
        Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
        OnNoOption = NoOptionBehavior.BlockAndAlert,
    };

    /// <summary>心上人触发不依赖夜晚计划（死亡批即处理）；给一个最小计划即可。</summary>
    private static StepMachineState Machine() => new()
    {
        Plan = new StepPlan
        {
            Label = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Slots = [StepSlot.Beat(new StepSlotId("dusk"))],
        },
        SlotIndex = 0,
        Quota = SlotQuotaState.Running,
        Control = ControlMode.Automatic,
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

    /// <summary>醉酒 / 中毒维度未观测的状态（不猜的输入）。</summary>
    private static GameState StateWithUnobservedStatus(
        params (int Seat, string Character, LifeState Life)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(Alignment.Good),
                    Life = Fact(row.Life),
                }),
            ],
        };

    /// <summary>已经挂着一条心上人醉酒效果的状态（幂等输入）。</summary>
    private static GameState StateWithEffect(
        params (int Seat, string Character, LifeState Life, DrunkState Drunk, PoisonState Poison)[] rows) =>
        State(rows) with
        {
            PersistentEffects =
            [
                new PersistentEffect
                {
                    Id = new EffectId("sweetheart:1:drunk"),
                    Source = SweetheartSeat,
                    Ability = new AbilityId("sweetheart"),
                    Target = new SeatId(2),
                    SourceCharacter = new CharacterId("sweetheart"),
                    Dimension = EffectDimension.Drunk,
                    SourceStateIndependent = true,
                },
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
