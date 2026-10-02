using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 哲学家的常驻醉酒（动态检测）回归：被获得角色的**当前持有者**醉酒；角色不在场时没有期望；
/// 判定不了时（多名存活持有者 / 载荷缺失）显式说明而不是猜；「获得能力」事实终止后期望清空。
/// </summary>
/// <remarks>
/// 来源：百科《哲学家》· 2026-10-01 抓取 · 运作方式 4 / 6 / 13；规则细节 1；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0036。
/// </remarks>
public sealed class PhilosopherDrunkSourceTests
{
    private static readonly PhilosopherDrunkSource Source = new();

    /// <summary>还没获得能力 → 没有期望（对账会把遗留的醉酒终止掉）。</summary>
    [Fact]
    public void NoGrant_NoExpectations()
    {
        var assessment = Source.Evaluate(Context(Ledger((1, "philosopher"), (2, "dreamer"))));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>被获得角色在场（唯一存活持有者）→ 那名玩家醉酒；标识稳定、归因到哲学家。</summary>
    [Fact]
    public void GrantedCharacterInPlay_MarksLivingHolderDrunk()
    {
        var state = Ledger((1, "philosopher"), (2, "dreamer")) with
        {
            PersistentEffects = [Grant(seat: 1, granted: new CharacterId("dreamer"))],
        };

        var assessment = Source.Evaluate(Context(state));

        var expectation = Assert.Single(assessment.Expectations);
        Assert.Equal(new EffectId("standing:philosopher.grant.drunk:1:2"), expectation.Id);
        Assert.Equal(new SeatId(1), expectation.Source);
        Assert.Equal(new SeatId(2), expectation.Target);
        Assert.Equal(new AbilityId("philosopher.grant.drunk"), expectation.Ability);
        Assert.Equal(new CharacterId("philosopher"), expectation.SourceCharacter);
        Assert.Equal(EffectDimension.Drunk, expectation.Dimension);
    }

    /// <summary>被获得角色还不在场 → 没有期望；它后来进场时由下一轮对账落下（运作方式 6）。</summary>
    [Fact]
    public void GrantedCharacterNotInPlay_NoExpectations()
    {
        var state = Ledger((1, "philosopher"), (2, "clockmaker")) with
        {
            PersistentEffects = [Grant(seat: 1, granted: new CharacterId("dreamer"))],
        };

        var assessment = Source.Evaluate(Context(state));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>被获得角色有多名存活持有者（角色唯一本不该发生）→ 判定不了，显式说明。</summary>
    [Fact]
    public void MultipleLivingHolders_Inconclusive()
    {
        var state = Ledger((1, "philosopher"), (2, "dreamer"), (3, "dreamer")) with
        {
            PersistentEffects = [Grant(seat: 1, granted: new CharacterId("dreamer"))],
        };

        var assessment = Source.Evaluate(Context(state));

        Assert.False(assessment.IsConclusive);
        Assert.Contains("角色唯一", assessment.Note, StringComparison.Ordinal);
    }

    /// <summary>唯一持有者已死亡 → 醉酒标记仍跟着它（「该角色对应的玩家」就是它），不转移到别人身上。</summary>
    [Fact]
    public void SingleDeadHolder_KeepsTheMarker()
    {
        var state = Ledger((1, "philosopher", LifeState.Alive), (2, "dreamer", LifeState.Dead)) with
        {
            PersistentEffects = [Grant(seat: 1, granted: new CharacterId("dreamer"))],
        };

        var assessment = Source.Evaluate(Context(state));

        var expectation = Assert.Single(assessment.Expectations);
        Assert.Equal(new SeatId(2), expectation.Target);
    }

    /// <summary>有存活持有者时优先它（规则细节 1：通常应优先让存活玩家醉酒）。</summary>
    [Fact]
    public void LivingHolderPreferredOverDeadOne()
    {
        var state = Ledger(
            (1, "philosopher", LifeState.Alive),
            (2, "dreamer", LifeState.Dead),
            (3, "dreamer", LifeState.Alive)) with
        {
            PersistentEffects = [Grant(seat: 1, granted: new CharacterId("dreamer"))],
        };

        var assessment = Source.Evaluate(Context(state));

        var expectation = Assert.Single(assessment.Expectations);
        Assert.Equal(new SeatId(3), expectation.Target);
    }

    /// <summary>「获得能力」事实已经终止（哲学家死亡 / 换角）→ 期望清空，那名玩家恢复清醒。</summary>
    [Fact]
    public void TerminatedGrant_NoExpectations()
    {
        var state = Ledger((1, "philosopher"), (2, "dreamer")) with
        {
            PersistentEffects =
            [
                Grant(seat: 1, granted: new CharacterId("dreamer")) with
                {
                    Termination = new EffectTermination
                    {
                        Kind = EffectTerminationKind.SourceDied,
                        Reason = "测试：来源死亡",
                    },
                },
            ],
        };

        var assessment = Source.Evaluate(Context(state));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>事实没有记下被获得的角色（不该发生）→ 判定不了，不猜。</summary>
    [Fact]
    public void GrantWithoutCharacter_Inconclusive()
    {
        var state = Ledger((1, "philosopher"), (2, "dreamer")) with
        {
            PersistentEffects = [Grant(seat: 1, granted: null)],
        };

        var assessment = Source.Evaluate(Context(state));

        Assert.False(assessment.IsConclusive);
        Assert.Contains("没有记下被获得的角色", assessment.Note, StringComparison.Ordinal);
    }

    private static StandingEffectContext Context(GameState state) => new()
    {
        State = state,
        Seats = [.. state.Seats.Select(entry => entry.Seat)],
    };

    private static PersistentEffect Grant(int seat, CharacterId? granted) => new()
    {
        Id = new EffectId($"philosopher.grant:{seat}"),
        Source = new SeatId(seat),
        Ability = new AbilityId("philosopher.grant"),
        Target = new SeatId(seat),
        SourceCharacter = new CharacterId("philosopher"),
        GrantedCharacter = granted,
    };

    private static GameState Ledger(params (int Seat, string Character)[] rows) =>
        Ledger([.. rows.Select(row => (row.Seat, row.Character, LifeState.Alive))]);

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
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
