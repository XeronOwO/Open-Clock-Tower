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

    /// <summary>从公开目录取结算契约（角色实现是 internal，测试只走注册表）。</summary>
    private static IAbilityResolution Contract() =>
        NightActions.Resolutions.Find(PitHag)
        ?? throw new InvalidOperationException("麻脸巫婆没有注册结算契约");

    private static AbilityResolutionContext Context(
        GameState state,
        string? choice,
        bool effective = true,
        StepPlan? plan = null,
        int slotIndex = 0) => new()
        {
            SlotId = new StepSlotId("pit-hag"),
            PlanLabel = "sv:night-2",
            Phase = GamePhase.OtherNight,
            Actor = new SeatId(1),
            ActorCharacter = PitHag,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            State = state,
            Outcome = new AbilityOutcome
            {
                Effective = effective,
                Malfunction = effective ? null : MalfunctionKind.Poisoned,
            },
            Choice = choice,
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

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
