using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 演示步骤表：规则层（Rules）尚未实现，这里用**显式占位**数据证明端到端链路。
/// </summary>
/// <remarks>
/// **不是规则数据**：真实的夜晚顺序表属 <c>OpenClockTower.Rules</c>，由后续票据提供；
/// 这里的槽位与选项只用于本票据的运行时验收，不得被当成《梦殒春宵》规则。
/// </remarks>
public static class DemoStepPlan
{
    /// <summary>构造一个演示首夜计划：1 号行动 + 其余空槽位 + 黎明等待。</summary>
    public static StepPlan CreateFirstNight(int seatCount)
    {
        var actor = new SeatId(1);
        var slots = new List<StepSlot>
        {
            StepSlot.Action(
                new StepSlotId("demo-seat-1"),
                actor,
                new ChoicePrompt
                {
                    Context = "演示：1 号玩家选择一名玩家",
                    Options =
                    [
                        new DecisionOption { Value = "seat:2", Preview = "选择 2 号玩家" },
                        new DecisionOption { Value = "seat:3", Preview = "选择 3 号玩家" },
                    ],
                    OnNoOption = NoOptionBehavior.Skip,
                },
                [
                    new SeatDependency
                    {
                        Seat = actor,
                        RequiredLife = LifeState.Alive,
                        RequiredCharacter = new CharacterId("demo-character"),
                    },
                ]),
        };

        for (var seat = 2; seat <= seatCount; seat++)
        {
            slots.Add(StepSlot.Empty(new StepSlotId($"demo-seat-{seat}")));
        }

        slots.Add(StepSlot.DawnWait(new StepSlotId("demo-dawn")));
        return new StepPlan
        {
            Label = "demo:night-1",
            Phase = GamePhase.FirstNight,
            Slots = slots,
        };
    }

    /// <summary>构造第二个夜晚计划（节奏对比用，槽位形状与首夜一致）。</summary>
    public static StepPlan CreateOtherNight(int seatCount) =>
        CreateFirstNight(seatCount) with { Label = "demo:night-2", Phase = GamePhase.OtherNight };
}
