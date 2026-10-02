using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 方古的「限一次」整局事实（口径见 <c>docs/standard/rulings.md</c> R-0034）：
/// 标记落下后跨阶段保留、整局不复用、重复落下即事件流损坏。
/// </summary>
public sealed class FangGuInfectionTests
{
    /// <summary>标记落下 → 步骤机状态记下它；跨阶段（夜 → 白天）继续保留。</summary>
    [Fact]
    public void Marker_IsRecorded_AndCarriedAcrossPhases()
    {
        var night = StepMachine.StartPhase(NightPlan(), previous: null, GameState.Empty).State;

        var marked = StepMachine.Apply(
            night,
            new FangGuInfectionRecordedEvent { Seat = new SeatId(3), Source = new SeatId(1) })!;

        var marker = Assert.IsType<FangGuInfection>(marked.FangGuInfection);
        Assert.Equal(new SeatId(3), marker.Seat);
        Assert.Equal(new SeatId(1), marker.Source);
        Assert.NotEmpty(marker.Note);

        var nextPhase = StepMachine.Apply(marked, new PhaseStartedEvent
        {
            Plan = DayPlan(),
            Control = ControlMode.Automatic,
        })!;

        Assert.Equal(marker, nextPhase.FangGuInfection);
    }

    /// <summary>整局只能落下一次：第二次侵染即事件流损坏，显式抛错。</summary>
    [Fact]
    public void SecondMarker_Throws()
    {
        var night = StepMachine.StartPhase(NightPlan(), previous: null, GameState.Empty).State;
        var marked = StepMachine.Apply(
            night,
            new FangGuInfectionRecordedEvent { Seat = new SeatId(3), Source = new SeatId(1) })!;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            marked,
            new FangGuInfectionRecordedEvent { Seat = new SeatId(2), Source = new SeatId(4) }));
    }

    /// <summary>还没有任何阶段时落标记 → 事件流顺序损坏（标记只能在夜里产生）。</summary>
    [Fact]
    public void MarkerWithoutPhase_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            null,
            new FangGuInfectionRecordedEvent { Seat = new SeatId(3), Source = new SeatId(1) }));
    }

    private static StepPlan NightPlan() => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots = [StepSlot.Beat(new StepSlotId("dusk"))],
    };

    private static StepPlan DayPlan() => new()
    {
        Label = "sv:day-1",
        Phase = GamePhase.Day,
        Slots = [StepSlot.Beat(new StepSlotId("day-open"))],
    };
}
