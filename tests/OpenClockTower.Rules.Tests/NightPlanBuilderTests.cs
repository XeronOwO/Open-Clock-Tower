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

    /// <summary>
    /// 方古的夜间行动契约已就位：其他夜晚在场 → 行动槽位（不再 `plan.contract_missing`）；
    /// 首个夜晚的顺序表上没有方古条目——「除首个夜晚外」由顺序表本身表达。
    /// </summary>
    [Fact]
    public void FangGuSlot_IsActionSlot_OnlyOnOtherNights()
    {
        var state = State((1, "fang-gu", LifeState.Alive));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 1));
        var fangGu = plan.Slots.Single(slot => slot.Id.Value == "fang-gu");
        Assert.Equal(StepSlotKind.Action, fangGu.Kind);
        Assert.Equal(new SeatId(1), fangGu.Actor);
        Assert.Equal(new CharacterId("fang-gu"), fangGu.Owner);
        Assert.Single(fangGu.Dependencies);

        var firstNight = Build(Request(state, seatCount: 1));
        Assert.DoesNotContain(firstNight.Slots, slot => slot.Id.Value == "fang-gu");
    }

    /// <summary>
    /// 方古侵染后的两个同名标记（已死亡的原方古 + 存活的新方古，R-0034）：这一格属于存活的那一位；
    /// 只有**多名存活持有者**才是真正的数据缺陷（角色唯一只约束存活持有者）。
    /// </summary>
    [Fact]
    public void FangGuTokens_DeadOriginalAndAliveSuccessor_BindToAlive()
    {
        var state = State((1, "fang-gu", LifeState.Dead), (2, "fang-gu", LifeState.Alive), (3, "klutz", LifeState.Alive));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 3));

        var fangGu = plan.Slots.Single(slot => slot.Id.Value == "fang-gu");
        Assert.Equal(StepSlotKind.Action, fangGu.Kind);
        Assert.Equal(new SeatId(2), fangGu.Actor);
        Assert.Equal(new CharacterId("fang-gu"), fangGu.Owner);
    }

    /// <summary>两具同名标记都死亡 → 空槽位照样走配额（不是数据缺陷）。</summary>
    [Fact]
    public void FangGuTokens_AllDead_BecomesEmptySlot()
    {
        var state = State((1, "fang-gu", LifeState.Dead), (2, "fang-gu", LifeState.Dead), (3, "vortox", LifeState.Alive));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 3));

        var fangGu = plan.Slots.Single(slot => slot.Id.Value == "fang-gu");
        Assert.Equal(StepSlotKind.Empty, fangGu.Kind);
        Assert.Equal(new CharacterId("fang-gu"), fangGu.Character);
    }

    /// <summary>已经死亡的行动者：空槽位，照样走配额（D-0013 §1）。</summary>
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

    /// <summary>
    /// 哲学家获得能力后（R-0036）：被获得角色的格**没有行动者**时由获得者代行——那一格是行动槽位、
    /// 行动者是哲学家、能力契约取被获得角色；他自己的格变成「本夜无行动」（Skip，配额照走）。
    /// </summary>
    [Fact]
    public void PhilosopherGrant_GrantedSlotFree_IsTakenOverByPhilosopher()
    {
        var state = GrantState(
            [(1, "philosopher", LifeState.Alive), (2, "klutz", LifeState.Alive)],
            granted: "dreamer");

        var plan = Build(Request(state, nightNumber: 2, seatCount: 2));

        var dreamer = plan.Slots.Single(slot => slot.Id.Value == "dreamer");
        Assert.Equal(StepSlotKind.Action, dreamer.Kind);
        Assert.Equal(new SeatId(1), dreamer.Actor);
        Assert.Equal(new CharacterId("dreamer"), dreamer.Owner);
        Assert.Equal(new CharacterId("philosopher"), dreamer.Character);
        Assert.Equal(new CharacterId("philosopher"), dreamer.Dependencies[0].RequiredCharacter);
        var grantedOptions = dreamer.Prompt!.Options.Select(option => option.Value).ToArray();
        Assert.DoesNotContain("seat:1", grantedOptions);
        Assert.DoesNotContain("decline", grantedOptions);

        var philosopher = plan.Slots.Single(slot => slot.Id.Value == "philosopher");
        Assert.Equal(StepSlotKind.Action, philosopher.Kind);
        Assert.Equal(new SeatId(1), philosopher.Actor);
        Assert.Empty(philosopher.Prompt!.Options);
        Assert.Equal(NoOptionBehavior.Skip, philosopher.Prompt.OnNoOption);
    }

    /// <summary>
    /// 被获得角色在场（存活持有者）→ 那一格仍归它（醉酒 → 能力不生效）；哲学家改在**自己的格**上代行
    /// 同一个能力（提示按代行者重算：筑梦师不能选自己）。
    /// </summary>
    [Fact]
    public void PhilosopherGrant_GrantedCharacterInPlay_DelegatesAtOwnSlot()
    {
        var state = GrantState(
            [(1, "philosopher", LifeState.Alive), (2, "dreamer", LifeState.Alive), (3, "klutz", LifeState.Alive)],
            granted: "dreamer");

        var plan = Build(Request(state, nightNumber: 2, seatCount: 3));

        var dreamer = plan.Slots.Single(slot => slot.Id.Value == "dreamer");
        Assert.Equal(new SeatId(2), dreamer.Actor);
        Assert.Equal(new CharacterId("dreamer"), dreamer.Owner);
        Assert.Equal(new CharacterId("dreamer"), dreamer.Character);

        var philosopher = plan.Slots.Single(slot => slot.Id.Value == "philosopher");
        Assert.Equal(new SeatId(1), philosopher.Actor);
        Assert.Equal(new CharacterId("dreamer"), philosopher.Owner);
        Assert.Equal(new CharacterId("philosopher"), philosopher.Character);
        Assert.Equal(
            ["seat:2", "seat:3"],
            philosopher.Prompt!.Options.Select(option => option.Value).ToArray());
    }

    /// <summary>被获得的能力是死亡触发型（理发师：触发格，不是行动格）→ 本夜无行动，也不动触发格。</summary>
    [Fact]
    public void PhilosopherGrant_TriggerOnlyAbility_NoAction()
    {
        var state = GrantState(
            [(1, "philosopher", LifeState.Alive), (2, "klutz", LifeState.Alive)],
            granted: "barber");

        var plan = Build(Request(state, nightNumber: 2, seatCount: 2));

        Assert.Equal(
            StepSlotKind.Trigger,
            plan.Slots.Single(slot => slot.Id.Value == "barber").Kind);

        var philosopher = plan.Slots.Single(slot => slot.Id.Value == "philosopher");
        Assert.Empty(philosopher.Prompt!.Options);
        Assert.Equal(NoOptionBehavior.Skip, philosopher.Prompt.OnNoOption);
    }

    /// <summary>还没获得能力 → 他自己的格是常规的「选择」契约（含摇头）。</summary>
    [Fact]
    public void PhilosopherWithoutGrant_HasChoicePrompt()
    {
        var state = State((1, "philosopher", LifeState.Alive), (2, "klutz", LifeState.Alive));

        var plan = Build(Request(state, nightNumber: 2, seatCount: 2));

        var philosopher = plan.Slots.Single(slot => slot.Id.Value == "philosopher");
        Assert.Contains(philosopher.Prompt!.Options, option => option.Value == "decline");
    }

    /// <summary>
    /// 「每局限一次」在醉酒 / 中毒期间被用掉（机会被浪费，百科《重要细节》三-3）→
    /// 他自己的格不再开选择，只剩一条可归因的无行动。
    /// </summary>
    [Fact]
    public void PhilosopherGrantWasted_NoSecondChoice()
    {
        var state = State((1, "philosopher", LifeState.Alive), (2, "klutz", LifeState.Alive)) with
        {
            AbilityUses = new AbilityUseLedger().RecordUse(
                new SeatId(1),
                new AbilityId("philosopher.grant"),
                effective: false),
        };

        var plan = Build(Request(state, nightNumber: 2, seatCount: 2));

        var philosopher = plan.Slots.Single(slot => slot.Id.Value == "philosopher");
        Assert.Empty(philosopher.Prompt!.Options);
        Assert.Equal(NoOptionBehavior.Skip, philosopher.Prompt.OnNoOption);
    }

    /// <summary>带「获得能力」事实的账（哲学家在第一个席位）。</summary>
    private static GameState GrantState((int Seat, string Character, LifeState Life)[] rows, string granted) =>
        State([.. rows.Select(row => (row.Seat, row.Character, (LifeState?)row.Life))]) with
        {
            PersistentEffects =
            [
                new PersistentEffect
                {
                    Id = new EffectId($"philosopher.grant:{rows[0].Seat}"),
                    Source = new SeatId(rows[0].Seat),
                    Ability = new AbilityId("philosopher.grant"),
                    Target = new SeatId(rows[0].Seat),
                    SourceCharacter = new CharacterId("philosopher"),
                    GrantedCharacter = new CharacterId(granted),
                },
            ],
        };

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
