using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 建表器：顺序表映射、空槽位规则、行动契约提示，以及"不知道就拒绝"的各条失败路径。
/// </summary>
public sealed class NightPlanBuilderTests
{
    /// <summary>构造一个只含已观测维度的状态账（开局的角色 + 生死）。</summary>
    private static GameState State(params (int Seat, string Character, LifeState? Life)[] seats) =>
        new()
        {
            Seats = seats.Select(item => new SeatStateEntry
            {
                Seat = new SeatId(item.Seat),
                Character = new StateFact<CharacterId>
                {
                    Value = new CharacterId(item.Character),
                    Reason = "setup.assignment",
                },
                Life = item.Life is { } life
                    ? new StateFact<LifeState> { Value = life, Reason = "setup.assignment" }
                    : null,
            }).ToArray(),
        };

    /// <summary>构造建表请求（席位默认 1..seatCount）。</summary>
    private static NightPlanRequest Request(
        GameState state,
        int nightNumber = 1,
        NightOrderVariant variant = NightOrderVariant.Original,
        int seatCount = 2) =>
        new()
        {
            NightNumber = nightNumber,
            Variant = variant,
            Seats = Enumerable.Range(1, seatCount).Select(number => new SeatId(number)).ToArray(),
            State = state,
            Actions = NightActions.Default,
        };

    /// <summary>建表：断言成功并取回计划。</summary>
    private static StepPlan Build(NightPlanRequest request)
    {
        var outcome = NightPlanBuilder.Build(request);
        Assert.Null(outcome.FailureCode);
        return Assert.IsType<StepPlan>(outcome.Plan);
    }

    /// <summary>建表：断言失败并取回原因码。</summary>
    private static string BuildFailure(NightPlanRequest request)
    {
        var outcome = NightPlanBuilder.Build(request);
        Assert.Null(outcome.Plan);
        return Assert.IsType<string>(outcome.FailureCode);
    }

    /// <summary>逐条对齐两套口径的顺序表：角色条目 → 行动或空槽位，其余 → 节拍 / 黎明。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void FirstNight_PlanMapsEveryOrderEntryToASlot(NightOrderVariant variant)
    {
        var state = State((1, "clockmaker", LifeState.Alive), (2, "dreamer", LifeState.Alive));

        var plan = Build(Request(state, variant: variant));
        var entries = NightOrderTable.For(GamePhase.FirstNight, variant);

        Assert.Equal("sv:night-1", plan.Label);
        Assert.Equal(GamePhase.FirstNight, plan.Phase);
        Assert.Equal(variant.ToString(), plan.Variant);
        Assert.Equal(entries.Count, plan.Slots.Count);
        Assert.Equal(entries.Count, plan.Slots.Select(slot => slot.Id.Value).Distinct(StringComparer.Ordinal).Count());

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var slot = plan.Slots[index];

            if (entry.Kind == NightOrderEntryKind.Dawn)
            {
                Assert.Equal(StepSlotKind.DawnWait, slot.Kind);
                continue;
            }

            if (entry.Kind != NightOrderEntryKind.CharacterAction)
            {
                Assert.Equal(StepSlotKind.Beat, slot.Kind);
                continue;
            }

            var character = entry.Character!.Value.Value;
            switch (character)
            {
                case "clockmaker":
                    Assert.Equal(StepSlotKind.Action, slot.Kind);
                    Assert.Equal(new SeatId(1), slot.Actor);
                    break;
                case "dreamer":
                    Assert.Equal(StepSlotKind.Action, slot.Kind);
                    Assert.Equal(new SeatId(2), slot.Actor);
                    break;
                default:
                    // 不在场：空槽位照样走配额（D-0013 §1）。
                    Assert.Equal(StepSlotKind.Empty, slot.Kind);
                    break;
            }
        }
    }

    /// <summary>其他夜晚用另一套顺序表，计划标签带夜晚序号。</summary>
    [Fact]
    public void OtherNight_UsesOtherNightTableAndLabelsTheNightNumber()
    {
        var state = State((1, "dreamer", LifeState.Alive));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 1));

        Assert.Equal("sv:night-2", plan.Label);
        Assert.Equal(GamePhase.OtherNight, plan.Phase);
        Assert.Equal(
            NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Original).Count,
            plan.Slots.Count);

        var dreamer = plan.Slots.Single(slot => slot.Id.Value == "dreamer");
        Assert.Equal(StepSlotKind.Action, dreamer.Kind);
        Assert.Equal(new SeatId(1), dreamer.Actor);
    }

    /// <summary>
    /// 理发师格是**触发格**：在场且存活也不要求行动契约（本人在场也不行动），
    /// 产出的槽位是触发槽位而不是空槽位（空槽位会在进入时被判成"有人却没有契约"而阻塞）。
    /// </summary>
    [Theory]
    [InlineData(LifeState.Alive)]
    [InlineData(LifeState.Dead)]
    public void BarberSlot_IsTriggerSlot_WithOrWithoutLivingHolder(LifeState life)
    {
        var state = State((1, "barber", life));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 1));

        var barber = plan.Slots.Single(slot => slot.Id.Value == "barber");
        Assert.Equal(StepSlotKind.Trigger, barber.Kind);
        Assert.Equal(new CharacterId("barber"), barber.Character);
        Assert.Null(barber.Actor);
        Assert.Null(barber.Prompt);
        Assert.Null(barber.Owner);
    }

    /// <summary>触发格同样不替未观测的生死猜：理发师的生死未知 → 拒绝建表（D-0015）。</summary>
    [Fact]
    public void BarberSlot_WithUnobservedLife_IsRejected()
    {
        var state = State((1, "barber", null));

        Assert.Equal("plan.life_unobserved", BuildFailure(Request(state, nightNumber: 2, seatCount: 1)));
    }

    /// <summary>已死亡的行动者：空槽位，照样走配额（D-0013 §1）。</summary>
    [Fact]
    public void DeadActor_BecomesEmptySlot()
    {
        var state = State((1, "clockmaker", LifeState.Alive), (2, "dreamer", LifeState.Dead));

        var plan = Build(Request(state));

        Assert.Equal(StepSlotKind.Empty, plan.Slots.Single(slot => slot.Id.Value == "dreamer").Kind);
    }

    /// <summary>生死未观测：拒绝建表，不允许替它猜（D-0015 的同一原则）。</summary>
    [Fact]
    public void UnobservedLife_IsRejectedInsteadOfGuessed()
    {
        var state = State((1, "clockmaker", LifeState.Alive), (2, "dreamer", null));

        Assert.Equal("plan.life_unobserved", BuildFailure(Request(state)));
    }

    /// <summary>席位没有角色：拒绝——"不在场"与"忘了分配"必须区分得开。</summary>
    [Fact]
    public void SeatWithoutCharacter_IsRejected()
    {
        var state = State((1, "clockmaker", LifeState.Alive));

        Assert.Equal("plan.seat_unassigned", BuildFailure(Request(state, seatCount: 2)));
    }

    /// <summary>在场且有夜晚行动的角色没有契约：拒绝，禁止静默当成空槽位。</summary>
    [Fact]
    public void InPlayNightCharacterWithoutContract_IsRejected()
    {
        var state = State((1, "mathematician", LifeState.Alive));

        Assert.Equal("plan.contract_missing", BuildFailure(Request(state, seatCount: 1)));
    }

    /// <summary>同一角色出现在两个席位（状态账数据缺陷）：拒绝，不产出歧义计划。</summary>
    [Fact]
    public void CharacterOnTwoSeats_IsRejected()
    {
        var state = State((1, "dreamer", LifeState.Alive), (2, "dreamer", LifeState.Alive));

        Assert.Equal("plan.character_duplicated", BuildFailure(Request(state)));
    }

    /// <summary>未知口径：拒绝（客户端可能塞进未定义枚举值）。</summary>
    [Fact]
    public void UnknownVariant_IsRejected()
    {
        var state = State((1, "clockmaker", LifeState.Alive));

        Assert.Equal("plan.variant_invalid", BuildFailure(Request(state, variant: (NightOrderVariant)999)));
    }

    /// <summary>筑梦师：合法选项 = 本局席位 − 自己，按席位号升序；行动者自身事实进入依赖。</summary>
    [Fact]
    public void DreamerPrompt_OffersEveryOtherSeatInOrder()
    {
        var state = State(
            (1, "dreamer", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "artist", LifeState.Alive),
            (4, "klutz", LifeState.Alive));

        var plan = Build(Request(state, seatCount: 4));
        var slot = plan.Slots.Single(item => item.Id.Value == "dreamer");
        var prompt = Assert.IsType<ChoicePrompt>(slot.Prompt);

        Assert.Equal(new[] { "seat:2", "seat:3", "seat:4" }, prompt.Options.Select(option => option.Value));
        Assert.Equal(NoOptionBehavior.BlockAndAlert, prompt.OnNoOption);
        Assert.Contains(
            slot.Dependencies,
            dependency => dependency.Seat == new SeatId(1)
                && dependency.RequiredLife == LifeState.Alive
                && dependency.RequiredCharacter == new CharacterId("dreamer"));
    }

    /// <summary>钟表匠：没有玩家选项，信息由说书人给出（裁定点，D-0002）。</summary>
    [Fact]
    public void ClockmakerPrompt_HandsTheDecisionToTheStoryteller()
    {
        var state = State((1, "clockmaker", LifeState.Alive), (2, "dreamer", LifeState.Alive));

        var plan = Build(Request(state));
        var slot = plan.Slots.Single(item => item.Id.Value == "clockmaker");
        var prompt = Assert.IsType<ChoicePrompt>(slot.Prompt);

        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
    }

    /// <summary>同一请求重复建表：产出等价计划（D-0008 纯函数）。</summary>
    [Fact]
    public void SameRequest_ProducesEquivalentPlans()
    {
        var state = State((1, "clockmaker", LifeState.Alive), (2, "dreamer", LifeState.Alive));
        var request = Request(state);

        var first = Build(request);
        var second = Build(request);

        Assert.Equal(first.Label, second.Label);
        Assert.Equal(first.Phase, second.Phase);
        Assert.Equal(first.Variant, second.Variant);
        Assert.Equal(
            first.Slots.Select(slot => $"{slot.Id.Value}|{slot.Kind}|{slot.Actor}|{slot.Prompt?.Context}"),
            second.Slots.Select(slot => $"{slot.Id.Value}|{slot.Kind}|{slot.Actor}|{slot.Prompt?.Context}"));
    }
}
