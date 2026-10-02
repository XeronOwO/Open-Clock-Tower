using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 步骤机结构等价比较的**字段覆盖**：新增的处决分类与两维选择必须进比较器，
/// 否则"重建前后一致 / 重放等价"会在这两个字段上失明（对抗性复核 F-2 的同类问题一次收口）。
/// </summary>
public sealed class StepMachineStateComparerTests
{
    /// <summary>处决分类不同 = 状态不等价（ExecutedKind 是白天账的一部分：常规 / 洗脑师处罚 / 畸形秀演员处罚）。</summary>
    [Fact]
    public void ExecutionKind_IsPartOfTheDayLedgerComparison()
    {
        var executed = StepMachine.Apply(
            DayPhaseFixture.StartDay(),
            new ExecutedEvent
            {
                DayNumber = 1,
                Seat = new SeatId(2),
                Kind = ExecutionKind.CerenovusMadness,
            })!;

        var tampered = executed with
        {
            Day = executed.Day! with
            {
                Days = [executed.Day!.Days[0] with { ExecutedKind = ExecutionKind.Day }],
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
