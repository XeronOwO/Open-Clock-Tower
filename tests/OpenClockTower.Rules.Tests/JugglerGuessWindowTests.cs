using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 「首个白天」的起算（R-0057-B 第 3 条）：该席位当前这次持有杂耍艺人之后的第一个白天。
/// </summary>
/// <remarks>
/// 走公开面（<see cref="RoleContracts.JugglerGuesses"/>）：规则层给内核的正是这一条答案。
/// 依据：百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力与范例 2（「首个白天」是该角色的第一个白天）。
/// </remarks>
public sealed class JugglerGuessWindowTests
{
    private static readonly SeatId Juggler = new(1);

    /// <summary>开局分配的角色：首个白天 = 第 1 天。</summary>
    [Fact]
    public void SetupAssigned_StartsOnTheFirstDay()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Life = LifeState.Alive,
                Character = new CharacterId("juggler"),
                Reason = "开局：说书人分配",
            },
            Day(1),
        ]);

        Assert.Equal(1, FirstHeldDay(state));
    }

    /// <summary>夜里拿到角色（麻脸巫婆 / 哲学家一类）：次日的白天是他的首个白天（范例 2）。</summary>
    [Fact]
    public void AcquiredDuringTheNight_StartsOnTheNextDay()
    {
        var state = GameStateMachine.Fold(
        [
            Setup("clockmaker"),
            Day(1),
            Night(2),
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Character = new CharacterId("juggler"),
                Reason = "麻脸巫婆把他变成杂耍艺人",
            },
        ]);

        Assert.Equal(2, FirstHeldDay(state));
    }

    /// <summary>白天拿到角色：当天就算他的首个白天。</summary>
    [Fact]
    public void AcquiredDuringTheDay_StartsOnThatDay()
    {
        var state = GameStateMachine.Fold(
        [
            Setup("clockmaker"),
            Day(1),
            Day(2),
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Character = new CharacterId("juggler"),
                Reason = "说书人上报换角",
            },
        ]);

        Assert.Equal(2, FirstHeldDay(state));
    }

    /// <summary>开局之后才第一次观测到这个角色：平台从"知道的那一天"起算（不猜更早）。</summary>
    [Fact]
    public void FirstObservedAfterTheGameStarted_StartsOnThatDay()
    {
        var state = GameStateMachine.Fold(
        [
            Setup(null),
            Day(1),
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Character = new CharacterId("juggler"),
                Reason = "说书人上报角色",
            },
        ]);

        Assert.Equal(1, FirstHeldDay(state));
    }

    /// <summary>换走又换回来：按**最近一次**拿到角色起算。</summary>
    [Fact]
    public void Reacquired_StartsOverFromTheLatestChange()
    {
        var state = GameStateMachine.Fold(
        [
            Setup("juggler"),
            Day(1),
            Day(2),
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Character = new CharacterId("artist"),
                Reason = "理发师交换",
            },
            Night(3),
            new SeatStateChangedEvent
            {
                Seat = Juggler,
                Character = new CharacterId("juggler"),
                Reason = "又换了回来",
            },
        ]);

        Assert.Equal(3, FirstHeldDay(state));
    }

    /// <summary>角色对不上（他不是杂耍艺人）：判不了 → null（由内核显式拒绝，不猜）。</summary>
    [Fact]
    public void NotTheJuggler_IsIndeterminate()
    {
        var state = GameStateMachine.Fold([Setup("clockmaker"), Day(1)]);

        Assert.Null(FirstHeldDay(state));
    }

    private static int? FirstHeldDay(GameState state) =>
        RoleContracts.JugglerGuesses
            .Single(source => source.Character == new CharacterId("juggler"))
            .FirstHeldDay(state, Juggler);

    /// <summary>开局：只报这个席位的角色与生死（<paramref name="character"/> 为 null = 角色没观测）。</summary>
    private static SeatStateChangedEvent Setup(string? character) => new()
    {
        Seat = Juggler,
        Life = LifeState.Alive,
        Character = character is null ? null : new CharacterId(character),
        Reason = "开局：说书人分配",
    };

    private static DayStartedEvent Day(int dayNumber) => new() { DayNumber = dayNumber };

    private static PhaseStartedEvent Night(int nightNumber) => new()
    {
        Plan = new StepPlan
        {
            Label = $"sv:night-{nightNumber}",
            Phase = nightNumber == 1 ? GamePhase.FirstNight : GamePhase.OtherNight,
            Slots = [],
        },
        Control = ControlMode.Automatic,
    };
}
