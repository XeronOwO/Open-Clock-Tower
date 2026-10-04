using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 状态账等价比较：重建报告用它回答"整本账是否与事件流一致"（D-0014 能力 3）。
/// </summary>
/// <remarks>
/// 判据必须**顺序无关**：重建是同一批事件的再折叠，枚举顺序相同只是巧合；
/// 值 / 原因 / 导致方 / 效果链接 / 终止事实任何一处不同都算分叉。
/// </remarks>
public sealed class GameStateComparerTests
{
    private static readonly SeatId SeatOne = new(1);
    private static readonly SeatId SeatTwo = new(2);
    private static readonly EffectId EffectOne = new("e1");

    /// <summary>null 与空账不是一回事：null = 没有账，空账 = 合法的"还没观测到任何东西"。</summary>
    [Fact]
    public void NullAndEmpty_OnlyMatchTheirOwnKind()
    {
        Assert.True(GameStateComparer.AreEquivalent(null, null));
        Assert.False(GameStateComparer.AreEquivalent(null, GameState.Empty));
        Assert.False(GameStateComparer.AreEquivalent(GameState.Empty, null));
        Assert.True(GameStateComparer.AreEquivalent(GameState.Empty, new GameState()));
    }

    /// <summary>席位账按席位号配对，列表顺序不同不影响等价；值不同必须判为分叉。</summary>
    [Fact]
    public void Seats_AreMatchedBySeatNumber_NotByListOrder()
    {
        var left = new GameState
        {
            Seats =
            [
                SeatEntry(SeatOne, life: Fact(LifeState.Alive, "开局分配")),
                SeatEntry(SeatTwo, life: Fact(LifeState.Dead, "夜袭")),
            ],
        };
        var reordered = new GameState
        {
            Seats =
            [
                SeatEntry(SeatTwo, life: Fact(LifeState.Dead, "夜袭")),
                SeatEntry(SeatOne, life: Fact(LifeState.Alive, "开局分配")),
            ],
        };
        var changed = new GameState
        {
            Seats =
            [
                SeatEntry(SeatOne, life: Fact(LifeState.Alive, "开局分配")),
                SeatEntry(SeatTwo, life: Fact(LifeState.Alive, "夜袭")),
            ],
        };

        Assert.True(GameStateComparer.AreEquivalent(left, reordered));
        Assert.False(GameStateComparer.AreEquivalent(left, changed));
    }

    /// <summary>一条维度事实逐字段比较：值、原因、导致方、效果链接缺一不可。</summary>
    [Fact]
    public void SeatFact_ValueReasonCausedByAndEffectLinkAllMatter()
    {
        var baseline = SeatEntry(
            SeatOne,
            poison: Fact(PoisonState.Poisoned, "投毒者", causedBy: SeatTwo, effectId: EffectOne));

        Assert.True(EquivalentWith(baseline, baseline with { }));

        var changedValue = baseline with { Poison = baseline.Poison! with { Value = PoisonState.Healthy } };
        var changedReason = baseline with { Poison = baseline.Poison! with { Reason = "换了个原因" } };
        var changedCausedBy = baseline with { Poison = baseline.Poison! with { CausedBy = null } };
        var changedEffectLink = baseline with { Poison = baseline.Poison! with { EffectId = null } };

        Assert.False(EquivalentWith(baseline, changedValue));
        Assert.False(EquivalentWith(baseline, changedReason));
        Assert.False(EquivalentWith(baseline, changedCausedBy));
        Assert.False(EquivalentWith(baseline, changedEffectLink));
    }

    /// <summary>未观测 ≠ 已观测：缺一维与多一维必须判为分叉，不能用默认值抹平。</summary>
    [Fact]
    public void SeatFact_UnobservedDimension_IsNotTheSameAsObservedValue()
    {
        var observedLife = SeatEntry(SeatOne, life: Fact(LifeState.Alive, "开局分配"));
        var observedLifeAndDrunk = SeatEntry(
            SeatOne,
            life: Fact(LifeState.Alive, "开局分配"),
            drunk: Fact(DrunkState.Drunk, "说书人裁定"));

        Assert.False(EquivalentWith(observedLife, observedLifeAndDrunk));
        Assert.True(EquivalentWith(observedLife, SeatEntry(SeatOne, life: Fact(LifeState.Alive, "开局分配"))));
    }

    /// <summary>疯狂要求按多重集合比：标识、来源与内容都要一致，重复次数也要一致。</summary>
    [Fact]
    public void Madnesses_AreComparedAsMultiset()
    {
        var first = Requirement("sv:night-1:cerenovus:madness", "钟表匠");
        var second = Requirement("sv:night-2:cerenovus:madness", "筑梦师");
        var left = SeatEntry(SeatOne, life: Fact(LifeState.Alive), madnesses: [first, second]);
        var reordered = SeatEntry(SeatOne, life: Fact(LifeState.Alive), madnesses: [second, first]);
        var shorter = SeatEntry(SeatOne, life: Fact(LifeState.Alive), madnesses: [first]);
        var changed = SeatEntry(
            SeatOne,
            life: Fact(LifeState.Alive),
            madnesses: [first, second with { ProveToBe = "理发师" }]);

        Assert.True(EquivalentWith(left, reordered));
        Assert.False(EquivalentWith(left, shorter));
        Assert.False(EquivalentWith(left, changed));
    }

    /// <summary>持续型效果按效果标识配对，含终止事实逐字段比较（终止即不可逆，账上必须一致）。</summary>
    [Fact]
    public void PersistentEffects_MatchById_AndTerminationFactMustMatch()
    {
        var terminated = Effect(
            EffectOne,
            new EffectTermination { Kind = EffectTerminationKind.SourceDied, Reason = "来源死亡" });
        var terminatedOther = Effect(
            EffectOne,
            new EffectTermination
            {
                Kind = EffectTerminationKind.StorytellerVoided,
                Reason = "说书人作废",
                CausedBy = SeatTwo,
            });

        Assert.True(GameStateComparer.AreEquivalent(
            new GameState { PersistentEffects = [Effect(EffectOne, null)] },
            new GameState { PersistentEffects = [Effect(EffectOne, null)] }));
        Assert.False(GameStateComparer.AreEquivalent(
            new GameState { PersistentEffects = [Effect(EffectOne, null)] },
            new GameState { PersistentEffects = [terminated] }));
        Assert.False(GameStateComparer.AreEquivalent(
            new GameState { PersistentEffects = [terminated] },
            new GameState { PersistentEffects = [terminatedOther] }));
        Assert.False(GameStateComparer.AreEquivalent(
            new GameState { PersistentEffects = [Effect(EffectOne, null)] },
            new GameState
            {
                PersistentEffects =
                [
                    Effect(EffectOne, null) with { SourceCharacter = new CharacterId("dreamer") },
                ],
            }));
    }

    /// <summary>即时型效果按效果标识配对；施加者 / 能力 / 作用对象任一不同都算分叉。</summary>
    [Fact]
    public void InstantaneousEffects_MatchById_AndAttributionMustMatch()
    {
        var effect = new InstantaneousEffect
        {
            Id = EffectOne,
            Source = SeatOne,
            Ability = new AbilityId("no-dashii"),
            Target = SeatTwo,
        };

        Assert.True(GameStateComparer.AreEquivalent(
            new GameState { InstantaneousEffects = [effect] },
            new GameState { InstantaneousEffects = [effect with { }] }));
        Assert.False(GameStateComparer.AreEquivalent(
            new GameState { InstantaneousEffects = [effect] },
            new GameState { InstantaneousEffects = [effect with { Target = SeatOne }] }));
        Assert.False(GameStateComparer.AreEquivalent(
            new GameState { InstantaneousEffects = [effect] },
            new GameState { InstantaneousEffects = [effect with { Ability = new AbilityId("clockmaker") }] }));
    }

    /// <summary>能力使用账按多重集合比：使用过 ≠ 生效过，条数与生效标志都要一致。</summary>
    [Fact]
    public void AbilityUses_CompareMultiplicityAndEffectiveness()
    {
        var effective = new AbilityUse
        {
            Seat = SeatOne,
            Ability = new AbilityId("clockmaker"),
            Effective = true,
        };
        var ineffective = effective with { Effective = false };
        var otherAbility = new AbilityUse
        {
            Seat = SeatTwo,
            Ability = new AbilityId("dreamer"),
            Effective = true,
        };

        var left = WithUses([effective, otherAbility]);

        Assert.True(GameStateComparer.AreEquivalent(left, WithUses([otherAbility, effective])));
        Assert.False(GameStateComparer.AreEquivalent(left, WithUses([ineffective, otherAbility])));
        Assert.False(GameStateComparer.AreEquivalent(left, WithUses([effective, effective, otherAbility])));
    }

    /// <summary>失效账同样按多重集合比：原因分类不同就是不同的账。</summary>
    [Fact]
    public void Malfunctions_CompareMultiplicityAndKind()
    {
        var open = new Malfunction
        {
            Seat = SeatOne,
            Ability = new AbilityId("dreamer"),
            Kind = MalfunctionKind.Open,
        };
        var drunk = open with { Kind = MalfunctionKind.Drunk };

        var left = WithMalfunctions([open]);

        Assert.True(GameStateComparer.AreEquivalent(left, WithMalfunctions([open with { }])));
        Assert.False(GameStateComparer.AreEquivalent(left, WithMalfunctions([drunk])));
        Assert.False(GameStateComparer.AreEquivalent(left, WithMalfunctions([open, open])));
    }

    /// <summary>黎明水位是失效账的语义：条目相同但窗口不同 → 数学家的数字不同，不算等价。</summary>
    [Fact]
    public void Malfunctions_CompareDawnWindow()
    {
        var entry = new Malfunction
        {
            Seat = SeatOne,
            Ability = new AbilityId("dreamer"),
            Kind = MalfunctionKind.Poisoned,
        };

        var noDawn = new GameState { Malfunctions = new MalfunctionLedger { Entries = [entry] } };
        var afterDawn = new GameState
        {
            Malfunctions = new MalfunctionLedger { Entries = [entry], SinceDawnStart = 1 },
        };

        Assert.True(GameStateComparer.AreEquivalent(
            noDawn,
            new GameState { Malfunctions = new MalfunctionLedger { Entries = [entry] } }));
        Assert.False(GameStateComparer.AreEquivalent(noDawn, afterDawn));
    }

    /// <summary>离场账参与等价判定：席位与离场顺序都要一致（重建报告不能漏比这张账）。</summary>
    [Fact]
    public void DepartedSeats_AreComparedInOrder()
    {
        var left = new GameState { DepartedSeats = [SeatOne, SeatTwo] };
        var same = new GameState { DepartedSeats = [SeatOne, SeatTwo] };
        var reordered = new GameState { DepartedSeats = [SeatTwo, SeatOne] };
        var missing = new GameState { DepartedSeats = [SeatOne] };

        Assert.True(GameStateComparer.AreEquivalent(left, same));
        Assert.False(GameStateComparer.AreEquivalent(left, reordered));
        Assert.False(GameStateComparer.AreEquivalent(left, missing));
    }

    private static bool EquivalentWith(SeatStateEntry left, SeatStateEntry right) =>
        GameStateComparer.AreEquivalent(
            new GameState { Seats = [left] },
            new GameState { Seats = [right] });

    private static GameState WithUses(IReadOnlyList<AbilityUse> entries) =>
        new() { AbilityUses = new AbilityUseLedger { Entries = entries } };

    private static GameState WithMalfunctions(IReadOnlyList<Malfunction> entries) =>
        new() { Malfunctions = new MalfunctionLedger { Entries = entries } };

    private static StateFact<T> Fact<T>(
        T value,
        string reason = "测试",
        SeatId? causedBy = null,
        EffectId? effectId = null)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = reason,
            CausedBy = causedBy,
            EffectId = effectId,
        };

    private static SeatStateEntry SeatEntry(
        SeatId seat,
        StateFact<LifeState>? life = null,
        StateFact<CharacterId>? character = null,
        StateFact<Alignment>? alignment = null,
        StateFact<DrunkState>? drunk = null,
        StateFact<PoisonState>? poison = null,
        IReadOnlyList<MadnessRequirement>? madnesses = null) =>
        new()
        {
            Seat = seat,
            Life = life,
            Character = character,
            Alignment = alignment,
            Drunk = drunk,
            Poison = poison,
            Madnesses = madnesses ?? [],
        };

    private static PersistentEffect Effect(EffectId id, EffectTermination? termination) =>
        new()
        {
            Id = id,
            Source = SeatOne,
            Ability = new AbilityId("no-dashii"),
            Target = SeatTwo,
            SourceCharacter = new CharacterId("no-dashii"),
            Termination = termination,
        };

    /// <summary>一条洗脑师开出的疯狂要求（R-0021）：身份 + 来源 + 到期日。</summary>
    private static MadnessRequirement Requirement(string id, string proveToBe) => new()
    {
        Id = new MadnessRequirementId(id),
        Seat = SeatOne,
        ProveToBe = proveToBe,
        Source = SeatTwo,
        SourceCharacter = new CharacterId("cerenovus"),
        Ability = new AbilityId("cerenovus.madness"),
        ExpiresAtDay = 2,
    };
}
