using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 步骤机结构等价比较的**字段覆盖**：新增的处决分类与两维选择必须进比较器，
/// 否则"重建前后一致 / 重放等价"会在这两个字段上失明（对抗性复核 F-2 的同类问题一次收口）。
/// </summary>
public sealed class StepMachineStateComparerTests
{
    /// <summary>处决账（席位 + 来源分类）不同 = 状态不等价（常规 / 洗脑师处罚 / 畸形秀演员处罚；R-0050 起
    /// 还可能是屠夫窗口里的第二次常规处决）。</summary>
    [Fact]
    public void ExecutionLedger_IsPartOfTheDayLedgerComparison()
    {
        var executed = StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExecutedEvent
            {
                DayNumber = 1,
                Seat = new SeatId(2),
                Kind = ExecutionKind.CerenovusMadness,
            })!;

        var day = executed.Day!.Days[0];
        var tampered = executed with
        {
            Day = executed.Day! with
            {
                Days =
                [
                    day with
                    {
                        Executions = [day.Executions[0] with { Kind = ExecutionKind.Day }],
                    },
                ],
            },
        };

        Assert.True(StepMachineStateComparer.AreEquivalent(executed, executed with { }));
        Assert.False(StepMachineStateComparer.AreEquivalent(executed, tampered));
    }

    /// <summary>两维选择（R-0021）也必须进比较器：只比第一维会让两维契约在重建校验里失明。</summary>
    [Fact]
    public void SecondaryOptions_ArePartOfTheRequestComparison()
    {
        var left = StepMachine.StartPhase(NightPlan(secondary: "clockmaker")).State;
        var right = StepMachine.StartPhase(NightPlan(secondary: "dreamer")).State;

        Assert.False(StepMachineStateComparer.AreEquivalent(left, right));
    }

    /// <summary>
    /// 方古的「限一次」整局事实（R-0034）必须进比较器：它是"要不要侵染"的判定输入，
    /// 漏比会让重建校验在这条事实上失明。
    /// </summary>
    [Fact]
    public void FangGuInfection_IsPartOfTheComparison()
    {
        var state = StepMachine.StartPhase(NightPlan(secondary: "clockmaker")).State;
        var marked = StepMachine.Apply(
            state,
            new FangGuInfectionRecordedEvent { Seat = new SeatId(3), Source = new SeatId(1) })!;

        Assert.True(StepMachineStateComparer.AreEquivalent(marked, marked with { }));
        Assert.False(StepMachineStateComparer.AreEquivalent(marked, marked with
        {
            FangGuInfection = marked.FangGuInfection! with { Seat = new SeatId(4) },
        }));
    }

    /// <summary>麻脸巫婆的裁量窗口与「今晚理发」事实同样进比较器（同类整机事实一次收口）。</summary>
    [Fact]
    public void MachineFacts_ArePartOfTheComparison()
    {
        var state = StepMachine.StartPhase(NightPlan(secondary: "clockmaker")).State;
        var opened = state with
        {
            PitHagNight = new PitHagNight
            {
                Source = new SeatId(1),
                ClosesAfterSlotIndex = 2,
                CasualtyAbility = new AbilityId("pit-hag.casualty"),
                Deferred = [],
            },
            BarberNight = new BarberNight { Source = new SeatId(2), Note = "测试：今晚理发" },
        };

        Assert.False(StepMachineStateComparer.AreEquivalent(state, opened));
        Assert.True(StepMachineStateComparer.AreEquivalent(opened, opened with { }));
        Assert.False(StepMachineStateComparer.AreEquivalent(opened, opened with
        {
            PitHagNight = opened.PitHagNight! with { ClosesAfterSlotIndex = 3 },
        }));
        Assert.False(StepMachineStateComparer.AreEquivalent(opened, opened with
        {
            BarberNight = opened.BarberNight! with { Source = new SeatId(4) },
        }));
    }

    private static StepPlan NightPlan(string secondary) => new()
    {
        Label = "sv:night-2",
        Phase = GamePhase.OtherNight,
        Slots =
        [
            StepSlot.Action(
                new StepSlotId("cerenovus"),
                new SeatId(1),
                new ChoicePrompt
                {
                    Context = "洗脑师的两维选择",
                    Options = [new DecisionOption { Value = "seat:2", Preview = "2 号玩家" }],
                    SecondaryOptions = [new DecisionOption { Value = secondary, Preview = secondary }],
                    OnNoOption = NoOptionBehavior.BlockAndAlert,
                },
                owner: new CharacterId("cerenovus")),
        ],
    };
}
