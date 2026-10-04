using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 处罚处决的状态迁移（R-0020）：白天占用当天上限并立即收口白天、夜晚不占任何白天的上限、
/// 依据不成立或判不了时显式拒绝且状态不变。
/// </summary>
public sealed class AdjudicatedExecutionMachineTests
{
    /// <summary>白天处罚：处决事实 + 死亡 + 关天 + 走完白天计划，一处都不能少。</summary>
    [Fact]
    public void DayPunishment_ConsumesTheDailyLimit_AndClosesTheDay()
    {
        var state = DayPhaseFixture.StartDay();
        var context = Context(applicable: true);

        var outcome = DayPhaseFixture.Apply(state, context, Input(2));

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var executed = Assert.Single(outcome.Events.OfType<ExecutedEvent>());
        Assert.Equal(1, executed.DayNumber!.Value);
        Assert.Equal(new SeatId(2), executed.Seat);
        Assert.Equal(ExecutionKind.CerenovusMadness, executed.Kind);

        var death = Assert.Single(outcome.Events.OfType<SeatStateChangedEvent>());
        Assert.Equal(new SeatId(2), death.Seat);
        Assert.Equal(LifeState.Dead, death.Life);
        Assert.Contains("测试：依据成立", death.Reason, StringComparison.Ordinal);

        Assert.Contains(outcome.Events, item => item is DayClosedEvent { DayNumber: 1 });
        Assert.Contains(outcome.Events, item => item is PhaseCompletedEvent);
        Assert.True(outcome.State.IsPlanCompleted);
        Assert.Null(outcome.State.Day!.OpenDay);

        var day = Assert.Single(outcome.State.Day.Days);
        Assert.Equal(new SeatId(2), day.Executed);
        Assert.Equal(ExecutionKind.CerenovusMadness, day.ExecutedKind);
    }

    /// <summary>还有提名没计票时先计票：平台不替说书人拍板计票结论（沿用白天票 F-2 的口径）。</summary>
    [Fact]
    public void DayPunishment_WithAnOpenNomination_IsRejected_AndStateIsUntouched()
    {
        var state = DayPhaseFixture.StartDay();
        var context = Context(applicable: true);
        var afterNomination = DayPhaseFixture.Nominate(state, context, 1, 2).State;

        var outcome = DayPhaseFixture.Apply(afterNomination, context, Input(2));

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("day.nomination_not_counted", outcome.RejectionCode);
        Assert.Same(afterNomination, outcome.State);
        Assert.Empty(outcome.Events);
    }

    /// <summary>
    /// 白天开着、但处决额度已经用掉 → 显式拒绝。
    /// 正常命令流程到不了这个状态（处罚与常规处决都会同时收口白天），这是对"事件流被改写"的防御性拒绝。
    /// </summary>
    [Fact]
    public void DayPunishment_WhenTheDayAlreadyExecuted_IsRejected()
    {
        var context = Context(applicable: true);
        var state = StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day })!;

        var outcome = DayPhaseFixture.Apply(state, context, Input(3));

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("day.execution_used", outcome.RejectionCode);
        Assert.Empty(outcome.Events);
    }

    /// <summary>夜晚处罚：不写白天账、不关天、不推进阶段——不占任何白天的上限（R-0020）。</summary>
    [Fact]
    public void Punishment_DuringANight_DoesNotAdvanceThePlanNorTouchTheDayLedger()
    {
        var context = Context(applicable: true);
        var night = StepMachine.StartPhase(DayPhaseFixture.NightPlan()).State;

        var outcome = DayPhaseFixture.Apply(night, context, Input(2));

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(0, outcome.State.SlotIndex);
        Assert.DoesNotContain(outcome.Events, item => item is SlotAdvancedEvent or PhaseCompletedEvent);
        var executed = Assert.Single(outcome.Events.OfType<ExecutedEvent>());
        Assert.Null(executed.DayNumber);
        Assert.Contains(outcome.Events, item => item is SeatStateChangedEvent { Seat.Value: 2, Life: LifeState.Dead });
        Assert.Null(outcome.State.Day);
    }

    /// <summary>已死亡的目标仍可被处罚处决：只记「被处决」，不重复记死亡（处决 ≠ 死亡，R-0020）。</summary>
    [Fact]
    public void Punishment_OnADeadSeat_RecordsOnlyTheExecution()
    {
        var deadContext = Context(applicable: true) with
        {
            State = DayPhaseFixture.StateOf((1, LifeState.Alive), (2, LifeState.Dead), (3, LifeState.Alive)),
        };
        var state = DayPhaseFixture.Close(DayPhaseFixture.StartDay(), deadContext).State;

        var outcome = DayPhaseFixture.Apply(state, deadContext, Input(2));

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Single(outcome.Events.OfType<ExecutedEvent>());
        Assert.Empty(outcome.Events.OfType<SeatStateChangedEvent>());
    }

    /// <summary>拒绝面：席位 / 依据契约 / 依据不成立 / 判定不了 / 生死未观测，全部显式、状态不变。</summary>
    [Fact]
    public void Rejections_AreExplicit()
    {
        var state = DayPhaseFixture.StartDay();

        Assert.Equal(
            "punishment.seat_unknown",
            DayPhaseFixture.CodeOf(DayPhaseFixture.Apply(state, Context(applicable: true), Input(9))));

        Assert.Equal(
            "punishment.source_unknown",
            DayPhaseFixture.CodeOf(DayPhaseFixture.Apply(
                state,
                Context(applicable: true) with { AdjudicatedExecutions = [] },
                Input(2))));

        Assert.Equal(
            "punishment.not_applicable",
            DayPhaseFixture.CodeOf(DayPhaseFixture.Apply(state, Context(applicable: false), Input(2))));

        Assert.Equal(
            "punishment.indeterminate",
            DayPhaseFixture.CodeOf(DayPhaseFixture.Apply(state, Context(applicable: null), Input(2))));

        // 座次里有 2 号，但账上只观测过 1 号：生死未观测 → 拒绝，不猜（D-0015）。
        var seatWithoutLife = Context(applicable: true) with
        {
            State = DayPhaseFixture.StateOf((1, LifeState.Alive)),
        };
        Assert.Equal(
            "punishment.life_unknown",
            DayPhaseFixture.CodeOf(DayPhaseFixture.Apply(state, seatWithoutLife, Input(2))));
    }

    private static SettlementContext Context(bool? applicable) =>
        DayPhaseFixture.Context((1, LifeState.Alive), (2, LifeState.Alive), (3, LifeState.Alive))
        with
        { AdjudicatedExecutions = [new FixedSource(applicable)] };

    private static PunishExecutionInput Input(int seat) => new()
    {
        Seat = new SeatId(seat),
        Source = MadnessPunishmentSource.Cerenovus,
        Note = "测试：处罚处决",
    };

    /// <summary>固定结论的依据契约替身：本文件判的是状态迁移，不是角色规则（规则在 Rules.Tests）。</summary>
    private sealed class FixedSource(bool? applicable) : IAdjudicatedExecutionSource
    {
        public MadnessPunishmentSource Source => MadnessPunishmentSource.Cerenovus;

        public AdjudicatedExecutionEligibility Evaluate(
            GameState state,
            IReadOnlyList<SeatId> seats,
            SeatId seat) => new()
            {
                Applicable = applicable,
                Note = applicable is true ? "测试：依据成立" : "测试：依据不成立",
                DeathReason = applicable is true ? "测试：依据成立" : null,
                CausedBy = applicable is true ? new SeatId(1) : null,
            };
    }
}
