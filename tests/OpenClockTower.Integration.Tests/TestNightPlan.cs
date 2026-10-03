using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集成测试夹具：一个最小夜晚计划（1 个行动槽位 + 空槽位 + 黎明）。
/// </summary>
/// <remarks>
/// <para>
/// **这是测试夹具，不是规则数据。** 真实的《梦殒春宵》夜晚顺序表在
/// <c>src/OpenClockTower.Rules</c>；按它建表（含角色分配与行动契约）属结算引擎
/// （docs/backlog/in-progress/settlement-engine.md）。夹具只用于驱动请求 / 节奏 / 恢复链路，
/// 禁止被当成规则，也禁止复制回生产代码。
/// </para>
/// <para>
/// 形状与旧演示表一致：1 个行动槽位（1 号，2 个选项）+ 其余空槽位 + 黎明槽位——
/// 依赖槽位数量与请求标识的既有验收用例只需替换标识前缀。
/// </para>
/// </remarks>
internal static class TestNightPlan
{
    /// <summary>构造测试首夜计划：1 号行动 + 其余空槽位 + 黎明等待。</summary>
    internal static StepPlan CreateFirstNight(int seatCount)
    {
        var actor = new SeatId(1);
        var slots = new List<StepSlot>
        {
            StepSlot.Action(
                new StepSlotId("test-seat-1"),
                actor,
                new ChoicePrompt
                {
                    Context = "测试夹具：1 号玩家选择一名玩家",
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
                        RequiredCharacter = new CharacterId("test-character"),
                    },
                ]),
        };

        for (var seat = 2; seat <= seatCount; seat++)
        {
            slots.Add(StepSlot.Empty(new StepSlotId($"test-seat-{seat}")));
        }

        slots.Add(StepSlot.DawnWait(new StepSlotId("test-dawn")));
        return new StepPlan
        {
            Label = "test:night-1",
            Phase = GamePhase.FirstNight,
            Slots = slots,
        };
    }

    /// <summary>构造测试首夜计划：1 号「由说书人决定」的裁定槽位（无选项）+ 其余空槽位 + 黎明等待。</summary>
    internal static StepPlan CreateDecisionFirstNight(int seatCount)
    {
        var actor = new SeatId(1);
        var slots = new List<StepSlot>
        {
            StepSlot.Action(
                new StepSlotId("test-seat-1"),
                actor,
                new ChoicePrompt
                {
                    Context = "测试夹具：1 号的能力结果由说书人决定",
                    Options = [],
                    OnNoOption = NoOptionBehavior.StorytellerDecides,
                },
                [new SeatDependency { Seat = actor, RequiredLife = LifeState.Alive }]),
        };

        for (var seat = 2; seat <= seatCount; seat++)
        {
            slots.Add(StepSlot.Empty(new StepSlotId($"test-seat-{seat}")));
        }

        slots.Add(StepSlot.DawnWait(new StepSlotId("test-dawn")));
        return new StepPlan
        {
            Label = "test:night-1",
            Phase = GamePhase.FirstNight,
            Slots = slots,
        };
    }

    /// <summary>
    /// 构造测试首夜计划：1 号槽位是**空槽位却绑着一名存活持有者的角色**（这一格没有行动契约）
    /// → 进入即产出阻塞报警（R-0009 BlockAndAlert；票据 terminal-hold-residue 的夹具入口）。
    /// </summary>
    /// <remarks>
    /// 与 <c>SlotEntryLedgerTests.EmptySlotWithLivingHolder_Blocks</c> 同一条生产置位路径
    /// （<see cref="StepSlotEntry"/> 的 <c>OrphanReason</c>）：调用方要把
    /// <paramref name="blockedCharacter"/> 分配给一名存活席位，否则这一格会安静地空着。
    /// </remarks>
    internal static StepPlan CreateBlockedFirstNight(int seatCount, string blockedCharacter)
    {
        var slots = new List<StepSlot>
        {
            StepSlot.Empty(new StepSlotId("test-seat-1"), new CharacterId(blockedCharacter)),
        };

        for (var seat = 2; seat <= seatCount; seat++)
        {
            slots.Add(StepSlot.Empty(new StepSlotId($"test-seat-{seat}")));
        }

        slots.Add(StepSlot.DawnWait(new StepSlotId("test-dawn")));
        return new StepPlan
        {
            Label = "test:night-1-blocked",
            Phase = GamePhase.FirstNight,
            Slots = slots,
        };
    }
}
