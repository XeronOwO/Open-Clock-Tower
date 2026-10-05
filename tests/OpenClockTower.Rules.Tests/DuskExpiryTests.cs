using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 夜尽收口（R-0052 第 4 条 / R-0054 第 2 条）：「直到下个黄昏」的窗口效果在**下一夜开始**时统一终止；
/// 开夜命令在**建表之前**先按同一份实现收口——否则建表期读到的还是上一夜的窗口（女裁缝会多开一格）。
/// </summary>
/// <remarks>通过公开目录 <see cref="RoleContracts.EventTriggers"/> 与 <see cref="DuskExpiry"/> 取实现。</remarks>
public sealed class DuskExpiryTests
{
    private static readonly SeatId Target = new(1);
    private static readonly SeatId Barista = new(2);

    /// <summary>账上仍存续的窗口全部终止一次；非窗口效果不动；重复求值是幂等的。</summary>
    [Fact]
    public void ExpireAll_TerminatesLiveWindowsOnce()
    {
        var plain = new PersistentEffect
        {
            Id = new EffectId("test:plain"),
            Source = new SeatId(3),
            Ability = new AbilityId("no-dashii.poison"),
            Target = new SeatId(4),
            SourceCharacter = new CharacterId("no-dashii"),
            Dimension = EffectDimension.Poison,
        };
        var state = Ledger(
            WindowEffect("test:barista-twice", EffectWindowKind.SecondAction),
            WindowEffect("test:regain", EffectWindowKind.RegainedAbility),
            plain);

        var events = DuskExpiry.ExpireAll(state);
        Assert.Equal(2, events.Count);

        var after = state;
        foreach (var gameEvent in events)
        {
            after = GameStateMachine.Apply(after, gameEvent);
        }

        Assert.True(after.PersistentEffects.Single(effect => effect.Id.Value == "test:barista-twice").IsTerminated);
        Assert.True(after.PersistentEffects.Single(effect => effect.Id.Value == "test:regain").IsTerminated);
        Assert.False(after.PersistentEffects.Single(effect => effect.Id.Value == "test:plain").IsTerminated);
        Assert.Empty(DuskExpiry.ExpireAll(after));
    }

    /// <summary>
    /// 到期族是**显式集合**：每个窗口族都必须在这里有明确归属——枚举加了新窗口而忘了表态时，
    /// 这条测试会红（规则靠可失败门禁，不靠自觉）。保留能力（R-0056）**不**随黄昏到期：
    /// 它只随亡骨魔失去能力、或该爪牙不再是爪牙角色而终止。
    /// </summary>
    [Fact]
    public void ExpireAll_CoversEveryWindowKindDeliberately()
    {
        var expected = new Dictionary<EffectWindowKind, bool>
        {
            [EffectWindowKind.AfflictionImmunity] = true,
            [EffectWindowKind.SecondAction] = true,
            [EffectWindowKind.RegainedAbility] = true,
            [EffectWindowKind.RetainedAbility] = false,
        };

        Assert.Equal(
            Enum.GetValues<EffectWindowKind>().OrderBy(value => value),
            expected.Keys.OrderBy(value => value));

        foreach (var (kind, expires) in expected)
        {
            var events = DuskExpiry.ExpireAll(Ledger(WindowEffect($"test:{kind}", kind)));
            Assert.Equal(expires ? 1 : 0, events.Count);
        }
    }

    /// <summary>「新的一夜开始」只认夜晚阶段的 PhaseStartedEvent；白天阶段不算。</summary>
    [Fact]
    public void NightStarted_OnlyMatchesNightPhases()
    {
        Assert.True(DuskExpiry.NightStarted(
            [Started(GamePhase.OtherNight)]));
        Assert.True(DuskExpiry.NightStarted(
            [Started(GamePhase.FirstNight)]));
        Assert.False(DuskExpiry.NightStarted(
            [Started(GamePhase.Day)]));
        Assert.False(DuskExpiry.NightStarted([]));
    }

    /// <summary>集骨者触发器：下一夜开始 → 终止重获窗口；重复求值不产出第二条。</summary>
    [Fact]
    public void BoneCollectorDuskTrigger_TerminatesRegainWindowOnNightStart()
    {
        var trigger = RoleContracts.EventTriggers.Single(candidate =>
            candidate.Ability == new AbilityId("bone-collector.regain"));
        var state = Ledger(WindowEffect("test:regain", EffectWindowKind.RegainedAbility));

        var first = trigger.Evaluate(new EventTriggerContext
        {
            State = state,
            Seats = [Target, Barista],
            Events = [Started(GamePhase.OtherNight)],
        });
        var termination = Assert.Single(first);
        Assert.Contains("R-0052 / R-0054", ((PersistentEffectTerminatedEvent)termination).Termination.Reason, StringComparison.Ordinal);

        var after = GameStateMachine.Apply(state, termination);
        Assert.Empty(trigger.Evaluate(new EventTriggerContext
        {
            State = after,
            Seats = [Target, Barista],
            Events = [Started(GamePhase.OtherNight)],
        }));
    }

    /// <summary>
    /// 过期窗口不许开下一夜的槽位（本批修掉的既有缺陷）：女裁缝用过一次 + 上一夜的「行动两次」
    /// 窗口仍在账上时，**收口后的账**建表只能给她「本夜无行动」，不能当成第二次机会再唤醒她。
    /// </summary>
    [Fact]
    public void StaleWindowAfterExpiry_DoesNotOpenTheUsedAbilitySlot()
    {
        var seamstress = new AbilityId("seamstress");
        var state = Ledger(WindowEffect("test:barista-twice", EffectWindowKind.SecondAction)) with
        {
            Seats =
            [
                Entry(Target, "seamstress"),
                Entry(Barista, "barista"),
            ],
            AbilityUses = new AbilityUseLedger().RecordUse(Target, seamstress, effective: true),
        };

        var expired = state;
        foreach (var gameEvent in DuskExpiry.ExpireAll(state))
        {
            expired = GameStateMachine.Apply(expired, gameEvent);
        }

        var outcome = NightPlanBuilder.Build(new NightPlanRequest
        {
            NightNumber = 2,
            Variant = NightOrderVariant.Original,
            Seats = [Target, Barista],
            State = expired,
            Actions = NightActions.Default,
        });

        var plan = Assert.IsType<StepPlan>(outcome.Plan);
        var slot = plan.Slots.Single(candidate => candidate.Id.Value == "seamstress");
        Assert.NotNull(slot.Prompt);
        Assert.False(slot.Prompt!.HasOptions);
    }

    private static PhaseStartedEvent Started(GamePhase phase) => new()
    {
        Plan = Plan(phase),
        Control = ControlMode.Automatic,
    };

    private static StepPlan Plan(GamePhase phase) => new()
    {
        Label = $"test:{phase}",
        Phase = phase,
        Slots = [],
    };

    private static PersistentEffect WindowEffect(string id, EffectWindowKind kind) => new()
    {
        Id = new EffectId(id),
        Source = Barista,
        Ability = kind == EffectWindowKind.RegainedAbility
            ? new AbilityId("bone-collector.regain")
            : new AbilityId("barista"),
        Target = Target,
        SourceCharacter = kind == EffectWindowKind.RegainedAbility
            ? new CharacterId("bone-collector")
            : new CharacterId("barista"),
        GrantedCharacter = kind == EffectWindowKind.RegainedAbility ? new CharacterId("seamstress") : null,
        Window = kind,
        SourceStateIndependent = kind == EffectWindowKind.RegainedAbility,
    };

    private static GameState Ledger(params PersistentEffect[] effects) => new()
    {
        Seats = [Entry(Target, "seamstress"), Entry(Barista, "barista")],
        PersistentEffects = effects,
    };

    private static SeatStateEntry Entry(SeatId seat, string character) => new()
    {
        Seat = seat,
        Character = Fact(new CharacterId(character)),
        Alignment = Fact(Alignment.Good),
        Life = Fact(LifeState.Alive),
        Drunk = Fact(DrunkState.Sober),
        Poison = Fact(PoisonState.Healthy),
    };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new() { Value = value, Reason = "测试夹具" };
}
