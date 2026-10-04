using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 集骨者的规则回归（R-0054）：每局限一次、仅其他夜晚；候选 = 已死亡的玩家；摇头不算使用；
/// 未生效不落窗口但照样消耗机会；生效时落一条「重获能力」窗口并把目标角色尚未进入的空槽
/// 绑成真实行动格（死者保持死亡）；用后即失去自身能力，咖啡师「行动两次」不产生第二次重获。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="NightActions"/> 取契约——与运行时取的是同一个对象。
/// 来源：百科《集骨者》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节。
/// </remarks>
public sealed class BoneCollectorNightActionTests
{
    private static readonly CharacterId BoneCollector = new("bone-collector");
    private static readonly AbilityId RegainAbility = new("bone-collector.regain");

    /// <summary>候选：在局座次里的死亡席位 + 摇头；存活席位不在候选里。</summary>
    [Fact]
    public void Prompt_OffersDeadSeatsAndDecline()
    {
        var state = Ledger(
            (1, "dreamer", LifeState.Alive),
            (2, "bone-collector", LifeState.Alive),
            (3, "clockmaker", LifeState.Dead));

        var prompt = PromptContract().BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(2),
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
            State = state,
        });

        var values = prompt.Options.Select(option => option.Value).ToArray();
        Assert.Equal(new[] { "seat:3", "decline" }, values);
    }

    /// <summary>摇头：什么都不发生、不计使用（之后的夜晚还可以再选）。</summary>
    [Fact]
    public void Decline_DoesNothing()
    {
        var state = Ledger((1, "dreamer", LifeState.Dead), (2, "bone-collector", LifeState.Alive));

        var events = Contract().Resolve(Context(state, "decline"));

        Assert.Empty(events);
        Assert.False(Contract().CountsAsUse(Context(state, "decline")));
    }

    /// <summary>
    /// 生效：落一条「重获能力」窗口（带被重获的角色、不压制维度、与来源状态无关）并在当夜激活
    /// 目标角色的空槽；被选玩家**不会**收到任何告知（来源：角色简介）。
    /// </summary>
    [Fact]
    public void Grant_AppliesRegainWindowAndActivatesTargetSlot()
    {
        var state = Ledger(
            (1, "witch", LifeState.Dead),
            (2, "bone-collector", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive),
            (4, "sage", LifeState.Alive),
            (5, "klutz", LifeState.Alive));
        var plan = BuildPlan(state, nightNumber: 2);
        var slotIndex = SlotIndex(plan, "bone-collector");

        var events = Contract().Resolve(Context(state, "seat:1", plan, slotIndex));

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(EffectWindowKind.RegainedAbility, applied.Effect.Window);
        Assert.Equal(new SeatId(2), applied.Effect.Source);
        Assert.Equal(new SeatId(1), applied.Effect.Target);
        Assert.Equal(BoneCollector, applied.Effect.SourceCharacter);
        Assert.Equal(new CharacterId("witch"), applied.Effect.GrantedCharacter);
        Assert.True(applied.Effect.SourceStateIndependent);
        Assert.Null(applied.Effect.Dimension);
        Assert.StartsWith("sv:night-2:bone-collector", applied.Effect.Id.Value, StringComparison.Ordinal);

        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(new StepSlotId("witch"), activation.SlotId);
        Assert.Equal(new SeatId(1), activation.Actor);
        Assert.True(activation.Prompt.HasOptions);

        Assert.Empty(events.OfType<InformationResultIssuedEvent>());
    }

    /// <summary>能力未生效（醉酒 / 中毒 / 死亡）：不落窗口；但选择已作出，机会照常消耗（失去能力标记）。</summary>
    [Fact]
    public void Grant_Ineffective_DoesNotApplyButStillCounts()
    {
        var state = Ledger((1, "dreamer", LifeState.Dead), (2, "bone-collector", LifeState.Alive));

        var events = Contract().Resolve(Context(state, "seat:1", effective: false));

        Assert.Empty(events);
        Assert.True(Contract().CountsAsUse(Context(state, "seat:1", effective: false)));
    }

    /// <summary>用后即失去自身能力：契约声明不支持二次结算（咖啡师「行动两次」不产生第二次重获）。</summary>
    [Fact]
    public void Contract_IsLimitedAndRefusesSecondAction()
    {
        Assert.False(Contract().SupportsSecondAction);
        Assert.True(Contract().IsLimitedPerGame);
    }

    /// <summary>
    /// 「每局限一次」的总次数已经到 2：重获照常落账，但目标这一格绑成无选项的显式跳过
    /// （不是不声不响地不唤醒，R-0054 第 4 条）。
    /// </summary>
    [Fact]
    public void Grant_OverLimit_ActivatesExplicitSkipSlot()
    {
        var state = Ledger((1, "seamstress", LifeState.Dead), (2, "bone-collector", LifeState.Alive)) with
        {
            AbilityUses = new AbilityUseLedger()
                .RecordUse(new SeatId(1), new AbilityId("seamstress"), effective: true)
                .RecordUse(new SeatId(1), new AbilityId("seamstress"), effective: true),
        };
        var plan = BuildPlan(state, nightNumber: 2);
        var slotIndex = SlotIndex(plan, "bone-collector");

        var events = Contract().Resolve(Context(state, "seat:1", plan, slotIndex));

        Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(new StepSlotId("seamstress"), activation.SlotId);
        Assert.False(activation.Prompt.HasOptions);
        Assert.Contains("R-0054", activation.Prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>存活目标 / 越界 / 非法编码：一律显式失败，不当成摇头。</summary>
    [Theory]
    [InlineData("seat:1")]
    [InlineData("seat:99")]
    [InlineData("not-a-seat")]
    public void Grant_IllegalTarget_Throws(string choice)
    {
        var state = Ledger((1, "dreamer", LifeState.Alive), (2, "bone-collector", LifeState.Alive));

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, choice)));
    }

    private static INightAction PromptContract() =>
        NightActions.Default.Find(BoneCollector)
        ?? throw new InvalidOperationException("集骨者没有注册提示契约");

    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(BoneCollector)
        ?? throw new InvalidOperationException("集骨者没有注册结算契约");

    private static int SlotIndex(StepPlan plan, string slotId)
    {
        for (var index = 0; index < plan.Slots.Count; index++)
        {
            if (plan.Slots[index].Id.Value == slotId)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"计划里没有槽位 {slotId}");
    }

    private static StepPlan BuildPlan(GameState state, int nightNumber)
    {
        var outcome = NightPlanBuilder.Build(new NightPlanRequest
        {
            NightNumber = nightNumber,
            Variant = NightOrderVariant.Original,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Actions = NightActions.Default,
        });
        Assert.Null(outcome.FailureCode);
        return Assert.IsType<StepPlan>(outcome.Plan);
    }

    private static AbilityResolutionContext Context(
        GameState state,
        string choice,
        StepPlan? plan = null,
        int slotIndex = 0,
        bool effective = true) =>
        new()
        {
            SlotId = new StepSlotId("bone-collector"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(2),
            ActorCharacter = BoneCollector,
            ActorOwnCharacter = BoneCollector,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
                Note = effective ? null : "来源中毒：能力未生效",
            },
            Choice = choice,
            DaysStarted = 1,
            Plan = plan,
            SlotIndex = slotIndex,
        };

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        GameStateMachine.Fold(
        [
            .. rows.Select(row => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(row.Seat),
                Character = new CharacterId(row.Character),
                Alignment = Alignment.Good,
                Life = row.Life,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "测试夹具",
            }),
        ]);
}
