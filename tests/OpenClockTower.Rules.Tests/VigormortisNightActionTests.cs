using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 亡骨魔的规则回归：夜杀统一出口、杀死爪牙时落「保留能力」窗口 + 击杀事实（含说书人选的中毒侧）、
/// 窗口排在死亡之前、非爪牙目标照常击杀、已死亡目标与未生效能力什么都不做、
/// 麻脸巫婆之夜按待定死亡带载荷。
/// </summary>
/// <remarks>
/// 来源：百科《亡骨魔》· 2026-10-01 抓取 · 角色能力 / 规则细节 11–24 / 角色简介；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0056。
/// </remarks>
public sealed class VigormortisNightActionTests
{
    private static readonly CharacterId Vigormortis = new("vigormortis");

    /// <summary>提示：全体席位按席位号升序，含自己与已死亡玩家（《重要细节》三-1）。</summary>
    [Fact]
    public void Prompt_OffersEverySeatIncludingSelfAndDead()
    {
        var state = Ledger((1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead));

        var action = NightActions.Default.Find(Vigormortis)
            ?? throw new InvalidOperationException("亡骨魔没有注册提示契约");
        var prompt = action.BuildPrompt(new NightActionContext
        {
            Actor = new SeatId(1),
            Seats = [new SeatId(1), new SeatId(2)],
            State = state,
        });

        Assert.Equal(["seat:1", "seat:2"], prompt.Options.Select(option => option.Value));
    }

    /// <summary>非爪牙目标：只有统一击杀出口的两条事件，不落保留能力窗口、也不记击杀事实。</summary>
    [Fact]
    public void KillNonMinion_ProducesOrdinaryKill()
    {
        var state = Ledger((1, "vigormortis", LifeState.Alive), (2, "clockmaker", LifeState.Alive));

        var events = Contract().Resolve(Context(state, "seat:2"));

        Assert.Single(events.OfType<InstantaneousEffectAppliedEvent>());
        var death = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Empty(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Empty(events.OfType<VigormortisKillRecordedEvent>());
    }

    /// <summary>
    /// 杀死爪牙：先落「保留能力」窗口（排在死亡**之前**，死亡折叠时才判得出"他没有失去能力"），
    /// 再记击杀事实（含说书人选的中毒侧），最后才是统一击杀出口。
    /// </summary>
    [Fact]
    public void KillMinion_PlacesRetentionBeforeDeath_AndRecordsSide()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive),
            (5, "dreamer", LifeState.Alive));

        var events = Contract().Resolve(Context(state, "seat:2", decision: "counter-clockwise"));

        Assert.IsType<PersistentEffectAppliedEvent>(events[0]);
        var window = ((PersistentEffectAppliedEvent)events[0]).Effect;
        Assert.Equal(EffectWindowKind.RetainedAbility, window.Window);
        Assert.Equal(new SeatId(1), window.Source);
        Assert.Equal(new SeatId(2), window.Target);
        Assert.Equal(Vigormortis, window.SourceCharacter);
        Assert.False(window.SourceStateIndependent);

        var recorded = Assert.IsType<VigormortisKillRecordedEvent>(events[1]);
        Assert.Equal(new SeatId(1), recorded.Demon);
        Assert.Equal(new SeatId(2), recorded.Minion);
        Assert.Equal(SeatRingDirection.CounterClockwise, recorded.Side);

        Assert.Equal(4, events.Count);
        Assert.IsType<InstantaneousEffectAppliedEvent>(events[2]);
        var death = Assert.IsType<SeatStateChangedEvent>(events[3]);
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
    }

    /// <summary>已死亡的玩家不会再次死亡：不击杀、也不放保留能力标记（术语《生死》）。</summary>
    [Fact]
    public void DeadTarget_DoesNothing()
    {
        var state = Ledger((1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead));

        Assert.Empty(Contract().Resolve(Context(state, "seat:2")));
    }

    /// <summary>能力不生效（中毒 / 醉酒 / 死亡）：玩家照常选完目标，但什么都不发生（《重要细节》三-1）。</summary>
    [Fact]
    public void IneffectiveAbility_DoesNothing()
    {
        var state = Ledger((1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Alive));

        Assert.Empty(Contract().Resolve(Context(state, "seat:2", effective: false)));
    }

    [Fact]
    public void InvalidChoice_Throws()
    {
        var state = Ledger((1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Alive));

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "not-a-seat")));
    }

    /// <summary>角色未观测：判不了是不是爪牙，显式失败、不猜（D-0015）。</summary>
    [Fact]
    public void UnobservedTargetCharacter_Throws()
    {
        var state = GameStateMachine.Fold(
        [
            SeatChanged(1, "vigormortis", LifeState.Alive),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "缺角色",
            },
        ]);

        Assert.Throws<InvalidOperationException>(() => Contract().Resolve(Context(state, "seat:2")));
    }

    /// <summary>目标是爪牙：追加裁定点给出**两个候选**（顺时针 / 逆时针最近的镇民，跳过非镇民）。</summary>
    [Fact]
    public void PostChoiceDecision_OffersBothNearestTownsfolk()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive),
            (4, "dreamer", LifeState.Alive));

        var prompt = Contract().BuildPostChoiceDecision(Context(state, "seat:2"));

        Assert.NotNull(prompt);
        Assert.Equal(["clockwise", "counter-clockwise"], prompt!.Options.Select(option => option.Value));
        Assert.Contains("3 号玩家", prompt.Options[0].Preview, StringComparison.Ordinal);
        Assert.Contains("4 号玩家", prompt.Options[1].Preview, StringComparison.Ordinal);
    }

    /// <summary>非爪牙目标、能力未生效、没有镇民：都不需要说书人再裁定一次。</summary>
    [Fact]
    public void PostChoiceDecision_IsNullWhenNothingToChoose()
    {
        var nonMinion = Ledger((1, "vigormortis", LifeState.Alive), (2, "clockmaker", LifeState.Alive));
        Assert.Null(Contract().BuildPostChoiceDecision(Context(nonMinion, "seat:2")));

        var minion = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive));
        Assert.Null(Contract().BuildPostChoiceDecision(Context(minion, "seat:2", effective: false)));

        var noTownsfolk = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "cerenovus", LifeState.Alive));
        Assert.Null(Contract().BuildPostChoiceDecision(Context(noTownsfolk, "seat:2")));
    }

    /// <summary>
    /// 全场只有一名镇民：顺时针与逆时针数到同一人，说书人的"选侧"没有实际区别 → 不额外裁定，
    /// 记录按顺时针落（确定性口径，R-0056）。
    /// </summary>
    [Fact]
    public void SingleTownsfolk_NeedsNoDecision_AndRecordsClockwise()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive));

        Assert.Null(Contract().BuildPostChoiceDecision(Context(state, "seat:2")));

        var events = Contract().Resolve(Context(state, "seat:2", decision: null));
        var recorded = Assert.Single(events.OfType<VigormortisKillRecordedEvent>());
        Assert.Equal(SeatRingDirection.Clockwise, recorded.Side);
    }

    /// <summary>场上没有镇民：没有中毒可言，但保留下能力照常落（侧记 null）。</summary>
    [Fact]
    public void NoTownsfolk_RecordsNullSide()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "cerenovus", LifeState.Alive));

        var events = Contract().Resolve(Context(state, "seat:2", decision: null));

        var recorded = Assert.Single(events.OfType<VigormortisKillRecordedEvent>());
        Assert.Null(recorded.Side);
        Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
    }

    /// <summary>说书人的裁定值认不出：显式失败，不猜（D-0015）。</summary>
    [Fact]
    public void UnknownSideDecision_Throws()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive),
            (4, "dreamer", LifeState.Alive));

        Assert.Throws<InvalidOperationException>(
            () => Contract().Resolve(Context(state, "seat:2", decision: "sideways")));
    }

    /// <summary>
    /// 麻脸巫婆之夜：击杀是**待定死亡**，「保留能力」载荷随它一起记（不在当场落窗口 / 记事实），
    /// 说书人确认时才按普通夜晚同一条路径落格（R-0030 第 2 条 / R-0056）。
    /// </summary>
    [Fact]
    public void PitHagNight_DefersWithRetentionPayload()
    {
        var state = Ledger(
            (1, "vigormortis", LifeState.Alive),
            (2, "witch", LifeState.Alive),
            (3, "clockmaker", LifeState.Alive),
            (4, "dreamer", LifeState.Alive));

        var events = Contract().Resolve(
            Context(state, "seat:2", decision: "clockwise", pitHagNightActive: true));

        var deferred = Assert.Single(events.OfType<DeferredDeathRecordedEvent>());
        Assert.Equal(new SeatId(2), deferred.Target);
        Assert.Null(deferred.Transformation);
        var retention = Assert.IsType<DeferredRetention>(deferred.Retention);
        Assert.Equal(SeatRingDirection.Clockwise, retention.Side);
        Assert.Equal(EffectWindowKind.RetainedAbility, retention.RetainEffect.Window);
        Assert.Equal(new SeatId(2), retention.RetainEffect.Target);

        Assert.Empty(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Empty(events.OfType<VigormortisKillRecordedEvent>());
    }

    /// <summary>从公开目录取结算契约（角色实现是 internal，测试只走注册表）。</summary>
    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(Vigormortis)
        ?? throw new InvalidOperationException("亡骨魔没有注册结算契约");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        string? decision = null,
        bool effective = true,
        bool pitHagNightActive = false) => new()
        {
            SlotId = new StepSlotId("vigormortis"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(1),
            ActorCharacter = Vigormortis,
            ActorOwnCharacter = Vigormortis,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
            },
            Choice = choice,
            Decision = decision,
            DaysStarted = 1,
            PitHagNightActive = pitHagNightActive,
        };

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        GameStateMachine.Fold([.. rows.Select(row => SeatChanged(row.Seat, row.Character, row.Life))]);

    private static SeatStateChangedEvent SeatChanged(int seat, string character, LifeState life) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId(character),
        Life = life,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "测试夹具",
    };
}
