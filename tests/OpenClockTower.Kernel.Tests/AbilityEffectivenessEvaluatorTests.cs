using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 能力生效判定（R-0004 的记录面）：存活 + 清醒 + 健康 = 生效；中毒 / 醉酒 / 死亡 = 不生效；
/// 同时中毒且醉酒 = 不生效且两条原因并列（R-0004 已闭合）；维度没观测齐 = 判不了（null，不猜）。
/// 死亡席位按「重获能力」窗口放行的口径另见 <c>RegainedAbilityTests</c>（R-0054）。
/// </summary>
public sealed class AbilityEffectivenessEvaluatorTests
{
    [Fact]
    public void AliveSoberHealthy_IsEffective()
    {
        var outcome = Evaluate(Entry());

        Assert.NotNull(outcome);
        Assert.True(outcome!.Effective);
        Assert.Empty(outcome.Malfunctions);
    }

    [Fact]
    public void Poisoned_IsIneffectiveAndClassifiedPoisoned()
    {
        var outcome = Evaluate(Entry(poison: PoisonState.Poisoned));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Effective);
        Assert.Equal(new[] { MalfunctionKind.Poisoned }, outcome.Malfunctions);
    }

    [Fact]
    public void Drunk_IsIneffectiveAndClassifiedDrunk()
    {
        var outcome = Evaluate(Entry(drunk: DrunkState.Drunk));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Effective);
        Assert.Equal(new[] { MalfunctionKind.Drunk }, outcome.Malfunctions);
    }

    [Fact]
    public void PoisonedAndDrunk_IsIneffectiveWithBothCauses()
    {
        var outcome = Evaluate(Entry(drunk: DrunkState.Drunk, poison: PoisonState.Poisoned));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Effective);
        Assert.Equal(
            new[] { MalfunctionKind.Poisoned, MalfunctionKind.Drunk },
            outcome.Malfunctions);
        Assert.Contains("同时中毒且醉酒", outcome.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Dead_IsIneffectiveWithoutMalfunctionRecord()
    {
        var outcome = Evaluate(Entry(life: LifeState.Dead));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Effective);
        Assert.Empty(outcome.Malfunctions);
        Assert.Contains("死亡", outcome.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void UnobservedDimensions_AreNotGuessed()
    {
        var outcome = Evaluate(Entry(observeAll: false));

        Assert.Null(outcome);
    }

    /// <summary>按「单席位账」做一次生效判定；重获窗口口径由 RegainedAbilityTests 单独覆盖。</summary>
    private static AbilityOutcome? Evaluate(SeatStateEntry entry) =>
        AbilityEffectivenessEvaluator.Evaluate(new GameState { Seats = [entry] }, entry);

    private static SeatStateEntry Entry(
        LifeState life = LifeState.Alive,
        DrunkState drunk = DrunkState.Sober,
        PoisonState poison = PoisonState.Healthy,
        bool observeAll = true) =>
        new()
        {
            Seat = new SeatId(1),
            Character = Fact(new CharacterId("dreamer")),
            Alignment = Fact(Alignment.Good),
            Life = observeAll ? Fact(life) : null,
            Drunk = observeAll ? Fact(drunk) : null,
            Poison = observeAll ? Fact(poison) : null,
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new() { Value = value, Reason = "test.setup" };
}
