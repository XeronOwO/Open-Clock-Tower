using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 怪咖免死来源（R-0048）：范围只覆盖流放致死；能力生效 / 当天裁定 / 观测不齐各有显式结论。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.DeathProtections"/> 取契约——与运行时取的是同一个对象。
/// 日账里的裁定记录直接构造；「裁定事件 → 日账」的折叠回归在内核用例（DayProtectionTests）。
/// </remarks>
public sealed class DeviantProtectionSourceTests
{
    private static readonly SeatId Target = new(3);

    private static IDeathProtectionSource Source =>
        RoleContracts.DeathProtections.Single(source => source.Character.Value == "deviant");

    [Fact]
    public void NonDeviantSeat_IsNoneOfItsBusiness()
    {
        var assessment = Source.Evaluate(Context(
            State((3, "clockmaker", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)),
            day: null));

        Assert.Null(assessment);
    }

    [Fact]
    public void ExecutionDeath_IsOutOfScope()
    {
        // 能力文本是「当天你不能被流放」：处决路径不归它管（R-0048 第 1 条）。
        var assessment = Source.Evaluate(Context(
            State((3, "deviant", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)),
            day: null,
            cause: DeathProtectionCause.Execution));

        Assert.Null(assessment);
    }

    [Fact]
    public void DeviantWithoutRuling_AwaitsTheStoryteller()
    {
        var assessment = Assert.IsType<DeathProtectionAssessment>(Source.Evaluate(Context(
            State((3, "deviant", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)),
            Day())));

        Assert.Equal(DeathProtectionOutcome.NeedsRuling, assessment.Outcome);
    }

    [Theory]
    [InlineData(true, DeathProtectionOutcome.Protected)]
    [InlineData(false, DeathProtectionOutcome.NotProtected)]
    public void DeviantWithRuling_ReflectsTheRuling(bool isProtected, DeathProtectionOutcome expected)
    {
        var assessment = Assert.IsType<DeathProtectionAssessment>(Source.Evaluate(Context(
            State((3, "deviant", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)),
            Day(new DayProtectionDecision { Seat = Target, Protected = isProtected }))));

        Assert.Equal(expected, assessment.Outcome);
    }

    [Theory]
    [InlineData(LifeState.Dead, DrunkState.Sober, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Poisoned)]
    public void IneffectiveDeviant_IsNotProtected(
        LifeState life,
        DrunkState drunk,
        PoisonState poison)
    {
        // 能力不生效时不需要再问「今天是否有趣」：直接不受保护，本次流放照常死亡。
        var assessment = Assert.IsType<DeathProtectionAssessment>(Source.Evaluate(Context(
            State((3, "deviant", life, drunk, poison)),
            Day(new DayProtectionDecision { Seat = Target, Protected = true }))));

        Assert.Equal(DeathProtectionOutcome.NotProtected, assessment.Outcome);
    }

    [Theory]
    [InlineData(null, LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)]
    [InlineData("deviant", null, null, null)]
    public void UnobservedFacts_AreIndeterminate(
        string? character,
        LifeState? life,
        DrunkState? drunk,
        PoisonState? poison)
    {
        // 角色或维度没观测齐 → 判定不了：既不死亡也不放行，内核显式拒绝（不猜）。
        var assessment = Assert.IsType<DeathProtectionAssessment>(Source.Evaluate(Context(
            State((3, character, life, drunk, poison)),
            Day())));

        Assert.Equal(DeathProtectionOutcome.Indeterminate, assessment.Outcome);
    }

    private static DeathProtectionContext Context(
        GameState state,
        DayRecord? day,
        DeathProtectionCause cause = DeathProtectionCause.Exile) => new()
        {
            State = state,
            Seats = [Target],
            Seat = Target,
            Cause = cause,
            Day = day,
        };

    private static DayRecord Day(params DayProtectionDecision[] decisions) => new()
    {
        DayNumber = 1,
        Status = DayStatus.Open,
        ProtectionDecisions = decisions,
    };

    private static GameState State(
        params (int Seat, string? Character, LifeState? Life, DrunkState? Drunk, PoisonState? Poison)[] seats) =>
        GameStateMachine.Fold(
        [
            .. seats.Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Character = item.Character is null ? null : new CharacterId(item.Character),
                Life = item.Life,
                Drunk = item.Drunk,
                Poison = item.Poison,
                Reason = "test.protection",
            }),
        ]);
}
