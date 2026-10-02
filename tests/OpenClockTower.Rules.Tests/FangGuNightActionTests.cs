using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 方古的规则回归：除首夜外每夜击杀、首次**成功**命中外来者时侵染（外来者变邪恶方古 + 原方古死亡）、
/// 「限一次」整局不复用、已死亡目标不触发、麻脸巫婆之夜窗口里的待定转化。
/// </summary>
/// <remarks>
/// 来源：百科《方古》· 2026-10-01 抓取 · 角色能力 / 运作方式 / 提示标记「限一次」/ 范例；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0029 / R-0030 / R-0034。
/// </remarks>
public sealed class FangGuNightActionTests
{
    private static readonly CharacterId FangGu = new("fang-gu");

    /// <summary>提示：全体席位按席位号升序，含自己与已死亡玩家（《重要细节》三-1）。</summary>
    [Fact]
    public void Prompt_OffersEverySeatIncludingSelfAndDead()
    {
        var state = LifeLedger(
            (1, "fang-gu", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "sweetheart", LifeState.Dead));

        var action = NightActions.Default.Find(FangGu)
            ?? throw new InvalidOperationException("方古没有注册提示契约");
        var prompt = action.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3)],
            State = state,
        });

        Assert.Equal(["seat:1", "seat:2", "seat:3"], prompt.Options.Select(option => option.Value));
    }

    /// <summary>普通目标（非外来者）：走统一击杀出口——即时型效果 + 死亡事实，归因为方古。</summary>
    [Fact]
    public void KillNonOutsider_ProducesOrdinaryKill()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(state, "seat:2"));

        Assert.Single(events.OfType<InstantaneousEffectAppliedEvent>());
        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Null(death.Character);
        Assert.Equal(new SeatId(1), death.CausedBy);
        Assert.Empty(events.OfType<FangGuInfectionRecordedEvent>());
    }

    /// <summary>
    /// 首次成功命中外来者：被攻击者**不死亡**，改为角色 + 阵营变化（邪恶方古）；原方古死亡；
    /// 「限一次」标记落下（整局事实）。
    /// </summary>
    [Fact]
    public void FirstOutsiderKill_ConvertsOutsider_AndKillsSource()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(state, "seat:3"));

        var changes = events.OfType<SeatStateChangedEvent>().ToArray();
        Assert.Equal(2, changes.Length);

        var converted = Assert.Single(changes, change => change.Seat == new SeatId(3));
        Assert.Equal(new CharacterId("fang-gu"), converted.Character);
        Assert.Equal(Alignment.Evil, converted.Alignment);
        Assert.Null(converted.Life);
        Assert.Equal(new SeatId(1), converted.CausedBy);

        var sourceDeath = Assert.Single(changes, change => change.Seat == new SeatId(1));
        Assert.Equal(LifeState.Dead, sourceDeath.Life);
        Assert.Null(sourceDeath.Character);
        Assert.Equal(new SeatId(1), sourceDeath.CausedBy);

        var marker = Assert.Single(events.OfType<FangGuInfectionRecordedEvent>());
        Assert.Equal(new SeatId(3), marker.Seat);
        Assert.Equal(new SeatId(1), marker.Source);
        Assert.Single(events.OfType<InstantaneousEffectAppliedEvent>());
    }

    /// <summary>「限一次」已用掉：新的方古再杀外来者，外来者正常死亡，不再侵染、也不再有第二条标记。</summary>
    [Fact]
    public void OutsiderKill_WhenMarkerConsumed_IsOrdinaryDeath()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(state, "seat:3", infectionConsumed: true));

        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(3), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Null(death.Character);
        Assert.Empty(events.OfType<FangGuInfectionRecordedEvent>());
    }

    /// <summary>
    /// 已死亡的目标不会再次死亡 → 没有「成功杀死」这回事：不侵染、原方古也不死
    /// （百科《方古》· 2026-10-01 抓取 · 范例：方古攻击已死亡的呆瓜）。
    /// </summary>
    [Fact]
    public void DeadOutsiderTarget_DoesNothing()
    {
        var state = LifeLedger(
            (1, "fang-gu", LifeState.Alive),
            (2, "clockmaker", LifeState.Alive),
            (3, "sweetheart", LifeState.Dead));

        var events = Contract().Resolve(Context(state, "seat:3"));

        Assert.Empty(events);
    }

    /// <summary>能力不生效（中毒 / 醉酒 / 死亡）：不击杀、不侵染、原方古不死。</summary>
    [Fact]
    public void IneffectiveAbility_DoesNothing()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(state, "seat:3", effective: false));

        Assert.Empty(events);
    }

    /// <summary>方古可以选自己：恶魔不是外来者，走普通击杀（自己没有"不死亡"的豁免）。</summary>
    [Fact]
    public void SelfKill_IsOrdinaryDeath()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"));

        var events = Contract().Resolve(Context(state, "seat:1"));

        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(1), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
    }

    /// <summary>
    /// 麻脸巫婆之夜窗口 + 首次命中外来者：记**携带转化载荷**的待定死亡（不落任何状态变化）；
    /// 说书人确认时按侵染结算，阻止时两者都不发生（R-0030 第 2 条 / R-0034）。
    /// </summary>
    [Fact]
    public void PitHagWindow_FirstOutsiderKill_IsDeferredWithTransformation()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(state, "seat:3", pitHagNightActive: true));

        var deferred = Assert.Single(events.OfType<DeferredDeathRecordedEvent>());
        Assert.Equal(new SeatId(3), deferred.Target);
        Assert.Equal(new SeatId(1), deferred.Source);
        var transformation = Assert.IsType<DeferredTransformation>(deferred.Transformation);
        Assert.Equal(new SeatId(3), transformation.Target);
        Assert.Equal(new CharacterId("fang-gu"), transformation.Character);
        Assert.Equal(Alignment.Evil, transformation.Alignment);
        Assert.Equal(new SeatId(1), transformation.Dies);
        Assert.Empty(events.OfType<SeatStateChangedEvent>());
        Assert.Empty(events.OfType<FangGuInfectionRecordedEvent>());
    }

    /// <summary>窗口 + 非外来者 / 标记已用 → 普通待定死亡（没有转化载荷，也不会再落标记）。</summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void PitHagWindow_OtherTargets_ArePlainDeferred(int targetSeat, bool consumed)
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        var events = Contract().Resolve(Context(
            state,
            $"seat:{targetSeat}",
            pitHagNightActive: true,
            infectionConsumed: consumed));

        var deferred = Assert.Single(events.OfType<DeferredDeathRecordedEvent>());
        Assert.Null(deferred.Transformation);
        Assert.Contains("方古", deferred.Note, StringComparison.Ordinal);
        Assert.Empty(events.OfType<FangGuInfectionRecordedEvent>());
    }

    /// <summary>生死未观测：不替它猜（D-0015 的同一原则）——整条命令失败。</summary>
    [Fact]
    public void UnobservedLife_Throws()
    {
        var state = UnobservedLifeLedger((1, "fang-gu"), (2, "clockmaker"), (3, "sweetheart"));

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "seat:3")));
    }

    /// <summary>选择不是合法席位编码 → 显式抛错，不静默当成"无事发生"。</summary>
    [Fact]
    public void InvalidChoice_Throws()
    {
        var state = NightLedger((1, "fang-gu"), (2, "clockmaker"));

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "not-a-seat")));
    }

    /// <summary>从公开目录取结算契约（角色实现是 internal，测试只走注册表）。</summary>
    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(FangGu)
        ?? throw new InvalidOperationException("方古没有注册结算契约");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        bool pitHagNightActive = false,
        bool infectionConsumed = false,
        StepPlan? plan = null,
        int slotIndex = 0) => new()
        {
            SlotId = new StepSlotId("fang-gu"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(1),
            ActorCharacter = FangGu,
            ActorOwnCharacter = FangGu,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
            },
            Choice = choice,
            DaysStarted = 1,
            Plan = plan,
            SlotIndex = slotIndex,
            PitHagNightActive = pitHagNightActive,
            FangGuInfectionConsumed = infectionConsumed,
        };

    private static GameState NightLedger(
        params (int Seat, string Character)[] rows) =>
        BuildLedger(
            [.. rows.Select(row => (row.Seat, row.Character, LifeState.Alive))],
            lifeObserved: true);

    /// <summary>带生死的账（需要"已死亡目标"这类行时用）。</summary>
    private static GameState LifeLedger(
        params (int Seat, string Character, LifeState Life)[] rows) =>
        BuildLedger(rows, lifeObserved: true);

    /// <summary>生死未观测的账（D-0015：不替它猜）。</summary>
    private static GameState UnobservedLifeLedger(
        params (int Seat, string Character)[] rows) =>
        BuildLedger(
            [.. rows.Select(row => (row.Seat, row.Character, LifeState.Alive))],
            lifeObserved: false);

    private static GameState BuildLedger(
        (int Seat, string Character, LifeState Life)[] rows,
        bool lifeObserved) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(Alignment.Evil),
                    Life = lifeObserved ? Fact(row.Life) : null,
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
