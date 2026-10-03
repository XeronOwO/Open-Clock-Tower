using System.Text.Json;
using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 死亡触发族新字段的快照 JSON 往返（D-0010 / D-0014）：
/// 贤者事实 / 心上人跳过账 / 触发型裁定点来源经过与宿主同一套 System.Text.Json 选项后结构等价，
/// 重启恢复与重建比较器因此不会在这三处失明。
/// </summary>
/// <remarks>
/// 序列化形状必须与 <c>OpenClockTower.Server.GameEventSerialization</c> 的快照口径一致
/// （<see cref="JsonSerializerDefaults.Web"/>），否则这里的"绿"不能代表真宿主。
/// </remarks>
public sealed class DeathTriggerStateJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Snapshot_RoundTrips_DeathTriggerAccounts()
    {
        var decision = new DecisionPoint
        {
            Id = new DecisionPointId("sweetheart:3"),
            Prompt = new ChoicePrompt
            {
                Context = "测试：心上人死亡裁定",
                Options =
                [
                    new DecisionOption { Value = "seat:4", Preview = "4 号玩家" },
                ],
                OnNoOption = NoOptionBehavior.BlockAndAlert,
            },
        };
        var state = new StepMachineState
        {
            Plan = new StepPlan
            {
                Label = "sv:night-2",
                Phase = GamePhase.OtherNight,
                Variant = "Original",
                Slots =
                [
                    StepSlot.Trigger(new StepSlotId("sweetheart"), new CharacterId("sweetheart")),
                    StepSlot.Trigger(new StepSlotId("sage"), new CharacterId("sage")),
                    StepSlot.DawnWait(new StepSlotId("dawn")),
                ],
            },
            SlotIndex = 1,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            AwaitingDecision = decision,
            AwaitingDecisionTriggerAbility = new AbilityId("sweetheart"),
            SageNight = new SageNight
            {
                Sage = new SeatId(2),
                Demon = new SeatId(5),
                DemonCharacter = new CharacterId("no-dashii"),
                Effective = false,
                Note = "测试：贤者被杀",
            },
            SweetheartSkips =
            [
                new SweetheartSkipRecord
                {
                    Sweetheart = new SeatId(3),
                    Reason = "测试：能力未生效",
                },
            ],
        };

        var json = JsonSerializer.Serialize(state, Options);
        var restored = JsonSerializer.Deserialize<StepMachineState>(json, Options);

        Assert.NotNull(restored);
        Assert.True(
            StepMachineStateComparer.AreEquivalent(state, restored),
            "贤者事实 / 心上人跳过账 / 触发型裁定点来源没有通过快照 JSON 往返");
    }

    [Fact]
    public void Snapshot_RoundTrips_NullDeathTriggerAccounts()
    {
        var state = new StepMachineState
        {
            Plan = new StepPlan
            {
                Label = "sv:night-2",
                Phase = GamePhase.OtherNight,
                Slots = [StepSlot.Beat(new StepSlotId("dusk"))],
            },
            SlotIndex = 0,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
        };

        var json = JsonSerializer.Serialize(state, Options);
        var restored = JsonSerializer.Deserialize<StepMachineState>(json, Options);

        Assert.NotNull(restored);
        Assert.Null(restored!.SageNight);
        Assert.Empty(restored.SweetheartSkips);
        Assert.Null(restored.AwaitingDecisionTriggerAbility);
        Assert.True(StepMachineStateComparer.AreEquivalent(state, restored));
    }
}
