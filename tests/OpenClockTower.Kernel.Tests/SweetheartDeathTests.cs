using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 心上人跳过账的机器级生命周期（平台口径见 <c>docs/standard/rulings.md</c> R-0039）：
/// 能力未生效 / 未裁定的跳过进账、同一名心上人只记一次、跨阶段保留；比较器看得见它。
/// </summary>
public sealed class SweetheartDeathTests
{
    /// <summary>跳过记录进账；同一名心上人不能重复记录（重复即事件流损坏）。</summary>
    [Fact]
    public void Skips_AreRecordedAndCannotRepeat()
    {
        var started = Start(NightPlan(StepFixture.Beat("dusk")));
        var skipped = StepMachine.Apply(started.State, new SweetheartDeathSkippedEvent
        {
            Sweetheart = new SeatId(1),
            Reason = "测试：能力未生效",
        })!;

        var record = Assert.Single(skipped.SweetheartSkips);
        Assert.Equal(new SeatId(1), record.Sweetheart);
        Assert.Contains("未生效", record.Reason, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            skipped,
            new SweetheartDeathSkippedEvent
            {
                Sweetheart = new SeatId(1),
                Reason = "测试：重复记录",
            }));
    }

    /// <summary>跳过账跨阶段保留：幂等依据不能随阶段遗忘。</summary>
    [Fact]
    public void Skips_CarryAcrossPhases()
    {
        var night = Start(NightPlan(StepFixture.Beat("dusk"), StepFixture.Beat("dawn")));
        var skipped = StepMachine.Apply(night.State, new SweetheartDeathSkippedEvent
        {
            Sweetheart = new SeatId(1),
            Reason = "测试：未裁定",
        })!;

        var next = StepMachine.StartPhase(
            NightPlan(StepFixture.Beat("dusk")),
            skipped,
            GameState.Empty);

        var record = Assert.Single(next.State.SweetheartSkips);
        Assert.Equal(new SeatId(1), record.Sweetheart);
    }

    /// <summary>比较器看得见跳过账：重建校验不能在这一族上失明。</summary>
    [Fact]
    public void Comparer_SeesSkips()
    {
        var started = Start(NightPlan(StepFixture.Beat("dusk")));
        var skipped = StepMachine.Apply(started.State, new SweetheartDeathSkippedEvent
        {
            Sweetheart = new SeatId(1),
            Reason = "测试：未生效",
        });

        Assert.False(StepMachineStateComparer.AreEquivalent(started.State, skipped));
    }

    private static StepMachineOutcome Start(StepPlan plan) =>
        StepMachine.StartPhase(plan, previous: null, GameState.Empty);

    private static StepPlan NightPlan(params StepSlot[] slots) => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = slots,
    };
}
