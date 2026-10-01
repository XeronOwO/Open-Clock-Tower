using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 诺-达鲺的常驻中毒：顺时针 / 逆时针各找最近的镇民（跳过非镇民、死亡也算）；不在场或无能力 → 无期望；
/// 角色没观测齐 → 判定不了（不猜）；邻近角色变化 → 期望跟着换人。
/// </summary>
public sealed class NoDashiiPoisonSourceTests
{
    private static readonly NoDashiiPoisonSource Source = new();

    [Fact]
    public void PoisonsNearestTownsfolkOnBothSides()
    {
        var assessment = Source.Evaluate(Context(
            (1, "no-dashii"),
            (2, "dreamer"),
            (3, "mutant"),
            (4, "klutz"),
            (5, "clockmaker")));

        Assert.True(assessment.IsConclusive);
        Assert.Equal(new[] { 2, 5 }, Targets(assessment));
        Assert.All(assessment.Expectations, expectation =>
        {
            Assert.Equal(new SeatId(1), expectation.Source);
            Assert.Equal(new CharacterId("no-dashii"), expectation.SourceCharacter);
            Assert.Equal(EffectDimension.Poison, expectation.Dimension);
        });
    }

    [Fact]
    public void SkipsNonTownsfolkNeighbours()
    {
        var assessment = Source.Evaluate(Context(
            (1, "no-dashii"),
            (2, "witch"),
            (3, "dreamer"),
            (4, "barber"),
            (5, "clockmaker")));

        Assert.Equal(new[] { 3, 5 }, Targets(assessment));
    }

    [Fact]
    public void DeadTownsfolkStillCount()
    {
        var assessment = Source.Evaluate(Context(
            [
                (1, "no-dashii"),
                (2, "dreamer"),
                (3, "mutant"),
                (4, "klutz"),
                (5, "clockmaker"),
            ],
            [2]));

        Assert.Contains(2, Targets(assessment));
    }

    [Fact]
    public void CharacterChangeMovesThePoison()
    {
        // 5 号从镇民（钟表匠）变成外来者（呆瓜）：逆时针最近的镇民落到 2 号，与顺时针同一人 → 只留一条。
        var assessment = Source.Evaluate(Context(
            (1, "no-dashii"),
            (2, "dreamer"),
            (3, "mutant"),
            (4, "barber"),
            (5, "klutz")));

        Assert.Equal(new[] { 2 }, Targets(assessment));
    }

    [Fact]
    public void NoDemonMeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            (1, "dreamer"),
            (2, "clockmaker"),
            (3, "mutant")));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    [Fact]
    public void DeadDemonMeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [
                (1, "no-dashii"),
                (2, "dreamer"),
                (3, "mutant"),
                (4, "klutz"),
                (5, "clockmaker"),
            ],
            [1]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    [Fact]
    public void UnobservedCharacter_IsInconclusive()
    {
        var state = GameStateMachine.Fold(
        [
            Seat(1, "no-dashii"),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "缺角色",
            },
        ]);

        var assessment = Source.Evaluate(new StandingEffectContext
        {
            State = state,
            Seats = [new SeatId(1), new SeatId(2)],
        });

        Assert.False(assessment.IsConclusive);
        Assert.Contains("角色尚未观测", assessment.Note, StringComparison.Ordinal);
    }

    private static int[] Targets(StandingEffectAssessment assessment) =>
        [.. assessment.Expectations.Select(expectation => expectation.Target.Value).OrderBy(value => value)];

    private static StandingEffectContext Context(
        params (int Seat, string Character)[] seats) =>
        Context(seats, []);

    private static StandingEffectContext Context(
        (int Seat, string Character)[] seats,
        int[] deadSeats)
    {
        var events = new List<GameEvent>();
        foreach (var (seat, character) in seats)
        {
            var changed = Seat(seat, character);
            events.Add(deadSeats.Contains(seat)
                ? changed with { Life = LifeState.Dead }
                : changed);
        }

        return new StandingEffectContext
        {
            State = GameStateMachine.Fold(events),
            Seats = [.. seats.Select(item => new SeatId(item.Seat)).OrderBy(seat => seat.Value)],
        };
    }

    private static SeatStateChangedEvent Seat(int seat, string character) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId(character),
        Life = LifeState.Alive,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "test.setup",
    };
}
