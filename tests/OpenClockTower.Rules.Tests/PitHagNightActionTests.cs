using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 麻脸巫婆角色变更的规则回归：不在场才变、阵营不变、在场无事发生、创造恶魔时激活其槽位。
/// 来源：百科《麻脸巫婆》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式；
/// 《重要细节》三-1（可选自己与已死亡玩家）与二（角色与阵营相互独立）。
/// </summary>
public sealed class PitHagNightActionTests
{
    private static readonly CharacterId PitHag = new("pit-hag");

    /// <summary>所选角色不在场 → 目标角色维度变化，阵营维度**不出现**，并带归因与变化前的角色。</summary>
    [Fact]
    public void TransformToAbsentCharacter_ChangesCharacterOnly()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:2|sage"));

        var changed = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), changed.Seat);
        Assert.Equal(new CharacterId("sage"), changed.Character);
        Assert.Equal(new CharacterId("clockmaker"), changed.PreviousCharacter);
        Assert.Null(changed.Alignment);
        Assert.Equal(new SeatId(1), changed.CausedBy);
    }

    /// <summary>所选角色已经在场 → 无事发生（也不是"未生效"）。</summary>
    [Fact]
    public void TransformToCharacterInPlay_DoesNothing()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:2|dreamer"));

        Assert.Empty(events);
    }

    /// <summary>能力不生效（中毒 / 醉酒 / 死亡）→ 什么都不发生。</summary>
    [Fact]
    public void IneffectiveAbility_DoesNothing()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:2|sage", effective: false));

        Assert.Empty(events);
    }

    /// <summary>可以把角色安在自己身上（《麻脸巫婆》提示与技巧：「转变你自己！」）。</summary>
    [Fact]
    public void TransformSelf_IsAllowed()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));

        var events = Contract().Resolve(Context(state, "seat:1|sage"));

        var changed = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(1), changed.Seat);
        Assert.Equal(new CharacterId("sage"), changed.Character);
    }

    /// <summary>角色表外的 slug → 显式抛错（不静默当成"不在场"）。</summary>
    [Fact]
    public void UnknownCharacter_Throws()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));

        Assert.Throws<InvalidOperationException>(() =>
            Contract().Resolve(Context(state, "seat:2|not-a-character")));
    }

    /// <summary>
    /// 创造出恶魔 → 它在今夜还没走到的那一格被激活（《夜晚行动顺序一览》麻脸巫婆条：
    /// 「否则，就需要唤醒这名玩家」；R-0030 第 6 条）。
    /// </summary>
    [Fact]
    public void TransformToDemon_ActivatesPendingSlot()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));
        var plan = OtherNightPlan();

        var events = Contract().Resolve(Context(state, "seat:2|vortox", plan: plan, slotIndex: 1));

        var activation = Assert.Single(events.OfType<SlotActivatedEvent>());
        Assert.Equal(2, activation.SlotIndex);
        Assert.Equal(new SeatId(2), activation.Actor);
        Assert.Equal(new CharacterId("vortox"), activation.Dependencies[0].RequiredCharacter);
    }

    /// <summary>创造的不是恶魔（例如镇民）时，只有角色变更、没有槽位激活。</summary>
    [Fact]
    public void TransformToTownsfolk_DoesNotActivate()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));
        var plan = OtherNightPlan();

        var events = Contract().Resolve(Context(state, "seat:2|sage", plan: plan, slotIndex: 1));

        Assert.Empty(events.OfType<SlotActivatedEvent>());
    }

    /// <summary>
    /// 创造恶魔 → 开一个到「最后一个能造成死亡的恶魔行动」为止的死亡裁量窗口（R-0030 第 1 条）。
    /// </summary>
    [Fact]
    public void TransformToDemon_OpensDeathAdjudicationWindow()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"));
        var plan = OtherNightPlan();

        var events = Contract().Resolve(Context(state, "seat:2|vortox", plan: plan, slotIndex: 1));

        var opened = Assert.Single(events.OfType<PitHagNightOpenedEvent>());
        Assert.Equal(new SeatId(1), opened.Source);
        Assert.Equal(2, opened.ClosesAfterSlotIndex);
        Assert.Equal(new AbilityId("pit-hag.casualty"), opened.CasualtyAbility);
    }

    /// <summary>窗口开启时，恶魔击杀改记**待定死亡**（不直接致死）——由说书人裁定（R-0030 第 2 条）。</summary>
    [Fact]
    public void DemonKill_WhileWindowOpen_IsDeferred()
    {
        var state = NightLedger((1, "pit-hag"), (2, "clockmaker"), (3, "vortox"));
        var vortox = NightActions.Resolutions.Find(new CharacterId("vortox"))
            ?? throw new InvalidOperationException("涡流没有注册结算契约");

        var events = vortox.Resolve(new AbilityResolutionContext
        {
            SlotId = new StepSlotId("vortox"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(3),
            ActorCharacter = new CharacterId("vortox"),
            ActorOwnCharacter = new CharacterId("vortox"),
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome { Effective = true },
            Choice = "seat:2",
            DaysStarted = 1,
            PitHagNightActive = true,
        });

        var deferred = Assert.Single(events.OfType<DeferredDeathRecordedEvent>());
        Assert.Equal(new SeatId(2), deferred.Target);
        Assert.Equal(new SeatId(3), deferred.Source);
        Assert.Empty(events.OfType<SeatStateChangedEvent>());
    }

    /// <summary>
    /// 创造镜像双子 → 开「选择对立双子」裁定（候选 = 与新双子阵营相对、含已死亡玩家）；
    /// 裁定落地为 <c>evil-twin.pair</c> 配对效果 + 双向互认（与首夜同源；E15 行 12）。
    /// </summary>
    [Fact]
    public void TransformToEvilTwin_OpensPairingDecision_AndAppliesPairing()
    {
        var state = AlignedLedger(
            (1, "pit-hag", Alignment.Evil),
            (2, "clockmaker", Alignment.Good),
            (3, "artist", Alignment.Good),
            (4, "no-dashii", Alignment.Evil),
            (5, "klutz", Alignment.Good));

        // 新双子（3 号艺术家）是善良 → 候选 = 邪恶玩家（1 号爪牙、4 号恶魔）。
        var prompt = Contract().BuildPostChoiceDecision(Context(state, "seat:3|evil-twin"));
        Assert.NotNull(prompt);
        Assert.Equal(["seat:1", "seat:4"], prompt!.Options.Select(option => option.Value));

        var events = Contract().Resolve(Context(state, "seat:3|evil-twin", decision: "seat:4"));

        var changed = Assert.Single(events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(3), changed.Seat);
        Assert.Equal(new CharacterId("evil-twin"), changed.Character);

        var applied = Assert.Single(events.OfType<PersistentEffectAppliedEvent>());
        Assert.Equal(new AbilityId("evil-twin.pair"), applied.Effect.Ability);
        Assert.Equal(new SeatId(3), applied.Effect.Source);
        Assert.Equal(new SeatId(4), applied.Effect.Target);
        Assert.Null(applied.Effect.Dimension);

        var information = events.OfType<InformationResultIssuedEvent>().ToArray();
        Assert.Equal(2, information.Length);
        Assert.Contains(
            information,
            item => item.Recipient == new SeatId(3)
                && item.Content.Contains("诺-达鲺", StringComparison.Ordinal));
        Assert.Contains(
            information,
            item => item.Recipient == new SeatId(4)
                && item.Content.Contains("镜像双子", StringComparison.Ordinal));
    }

    /// <summary>所选角色（镜像双子）已经在场 → 无事发生，也不再开配对裁定。</summary>
    [Fact]
    public void TransformToEvilTwinInPlay_DoesNotOpenPairingDecision()
    {
        var state = AlignedLedger(
            (1, "pit-hag", Alignment.Evil),
            (2, "evil-twin", Alignment.Good),
            (3, "no-dashii", Alignment.Evil));

        Assert.Null(Contract().BuildPostChoiceDecision(Context(state, "seat:3|evil-twin")));
        Assert.Empty(Contract().Resolve(Context(state, "seat:3|evil-twin")));
    }

    /// <summary>裁定不是合法候选（同阵营）→ 显式抛错，不静默落一条坏配对（D-0015）。</summary>
    [Fact]
    public void TransformToEvilTwin_IllegalPairing_Throws()
    {
        var state = AlignedLedger(
            (1, "pit-hag", Alignment.Evil),
            (2, "clockmaker", Alignment.Good),
            (3, "artist", Alignment.Good));

        Assert.Throws<InvalidOperationException>(() =>
            Contract().Resolve(Context(state, "seat:3|evil-twin", decision: "seat:2")));
    }

    /// <summary>从公开目录取结算契约（角色实现是 internal，测试只走注册表）。</summary>
    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(PitHag)
        ?? throw new InvalidOperationException("麻脸巫婆没有注册结算契约");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        StepPlan? plan = null,
        int slotIndex = 0,
        string? decision = null) => new()
        {
            SlotId = new StepSlotId("pit-hag"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(1),
            ActorCharacter = PitHag,
            ActorOwnCharacter = PitHag,
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
            Plan = plan,
            SlotIndex = slotIndex,
        };

    /// <summary>其他夜晚的计划：节拍 → 麻脸巫婆 → 涡流（空槽位，供激活测试）。</summary>
    private static StepPlan OtherNightPlan() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Beat(new StepSlotId("dusk")),
            StepSlot.Empty(new StepSlotId("pit-hag"), new CharacterId("pit-hag")),
            StepSlot.Empty(new StepSlotId("vortox"), new CharacterId("vortox")),
        ],
    };

    private static GameState NightLedger(params (int Seat, string Character)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(Alignment.Evil),
                    Life = Fact(LifeState.Alive),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    /// <summary>带真实阵营的账：镜像双子的配对候选要看阵营（NightLedger 把所有人都当邪恶，不够用）。</summary>
    private static GameState AlignedLedger(params (int Seat, string Character, Alignment Alignment)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(row.Alignment),
                    Life = Fact(LifeState.Alive),
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
