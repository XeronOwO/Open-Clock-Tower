using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 屠夫开窗来源（R-0050）：只有「在局 + 存活 + 能力生效」的屠夫可用；没有屠夫时与本来源无关；
/// 观测不齐、或无法排除屠夫时判定不了（不猜）。
/// </summary>
/// <remarks>
/// 通过公开目录 <see cref="RoleContracts.ExtraNominations"/> 取契约——与运行时取的是同一个对象。
/// 「开窗 → 额外提名 → 二次处决」的折叠与迁移回归在内核用例（ButcherWindowTests）。
/// </remarks>
public sealed class ButcherExtraNominationSourceTests
{
    private static IExtraNominationSource Source =>
        RoleContracts.ExtraNominations.Single(source => source.Character.Value == "butcher");

    [Fact]
    public void NonButcherGame_IsNoneOfItsBusiness()
    {
        var assessment = Source.Evaluate(Context(State(
            (1, "clockmaker", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy),
            (2, "dreamer", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy))));

        Assert.Null(assessment);
    }

    [Fact]
    public void AliveSoberHealthyButcher_IsAvailable()
    {
        var assessment = Assert.IsType<ExtraNominationAssessment>(Source.Evaluate(Context(State(
            (1, "butcher", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)))));

        Assert.Equal(ExtraNominationOutcome.Available, assessment.Outcome);
        Assert.Equal(new SeatId(1), assessment.Seat);
    }

    [Theory]
    [InlineData(LifeState.Dead, DrunkState.Sober, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Drunk, PoisonState.Healthy)]
    [InlineData(LifeState.Alive, DrunkState.Sober, PoisonState.Poisoned)]
    public void IneffectiveButcher_IsUnavailable(
        LifeState life,
        DrunkState drunk,
        PoisonState poison)
    {
        // 死亡 / 醉酒 / 中毒 → 能力不生效：照常关闭白天，不给额外提名（R-0050 第 1 条）。
        var assessment = Assert.IsType<ExtraNominationAssessment>(Source.Evaluate(Context(State(
            (1, "butcher", life, drunk, poison)))));

        Assert.Equal(ExtraNominationOutcome.Unavailable, assessment.Outcome);
    }

    [Theory]
    [InlineData(null, LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)]
    [InlineData("butcher", null, null, null)]
    public void UnobservedFacts_AreIndeterminate(
        string? character,
        LifeState? life,
        DrunkState? drunk,
        PoisonState? poison)
    {
        // 角色或维度没观测齐 → 判定不了：不静默关账，内核显式拒绝（不猜）。
        var assessment = Assert.IsType<ExtraNominationAssessment>(Source.Evaluate(Context(State(
            (1, character, life, drunk, poison)))));

        Assert.Equal(ExtraNominationOutcome.Indeterminate, assessment.Outcome);
    }

    [Fact]
    public void AnUnobservedSeatThatMightHideTheButcher_IsIndeterminate()
    {
        // 在局座次里的 2 号没有任何观测：无法排除其中藏着屠夫，不能默认"没有屠夫"（D-0015）。
        var context = new ExtraNominationContext
        {
            State = State((1, "clockmaker", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)),
            Seats = [new SeatId(1), new SeatId(2)],
            ExecutedSeat = new SeatId(1),
        };

        var assessment = Assert.IsType<ExtraNominationAssessment>(Source.Evaluate(context));

        Assert.Equal(ExtraNominationOutcome.Indeterminate, assessment.Outcome);
    }

    [Fact]
    public void TwoButchers_AreIndeterminate()
    {
        // 花名册不出重复角色；出现两个屠夫席位就是数据异常，按不猜处理。
        var assessment = Assert.IsType<ExtraNominationAssessment>(Source.Evaluate(Context(State(
            (1, "butcher", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy),
            (2, "butcher", LifeState.Alive, DrunkState.Sober, PoisonState.Healthy)))));

        Assert.Equal(ExtraNominationOutcome.Indeterminate, assessment.Outcome);
    }

    private static ExtraNominationContext Context(GameState state) => new()
    {
        State = state,
        Seats = [.. state.Seats.Select(entry => entry.Seat).OrderBy(seat => seat.Value)],
        ExecutedSeat = new SeatId(1),
    };

    private static GameState State(
        params (int Seat, string? Character, LifeState? Life, DrunkState? Drunk, PoisonState? Poison)[] seats) =>
        GameStateMachine.Fold(
        [
            .. seats.Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Character = item.Character is { } character ? new CharacterId(character) : null,
                Life = item.Life,
                Drunk = item.Drunk,
                Poison = item.Poison,
                Reason = "test.butcher-window",
            }),
        ]);
}
