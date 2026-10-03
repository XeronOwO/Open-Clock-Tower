using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 会话跟踪器的「槽位进入」状态：当前槽位的起算时刻与**本次进入的事件序号**。
/// </summary>
/// <remarks>
/// 进入序号是配额输入的幂等键分量：触发格应答重进本格必须换新序号，否则第二次配额会被当成
/// 重复命令回放、计划永久停在原地
/// （回归见 <c>BarberHostTests.BarberSwapAnsweredAfterQuotaElapsed_PlanStillAdvances</c>）。
/// </remarks>
public sealed class SessionTrackersSlotEntryTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>增量提交与全量重放得到同一起算点；重进本格换新序号；Clear 复位。</summary>
    [Fact]
    public void SlotEntrySequence_TracksEachEntry_AndSurvivesRecover()
    {
        GameEvent[] events =
        [
            Night(GamePhase.OtherNight),
            SlotEntered(7, "barber"),
            SlotEntered(9, "barber"), // 应答后的「重进本格」：同一槽位、新一次进入
        ];

        var stored = events
            .Select((gameEvent, index) => new StoredEvent
            {
                Sequence = index + 1,
                Event = gameEvent,
                RecordedAt = Epoch,
            })
            .ToArray();

        var recovered = new SessionTrackers();
        recovered.Recover(stored, machine: null);

        var incremental = new SessionTrackers();
        foreach (var item in stored)
        {
            incremental.Update([Draft(item.Sequence, item.Event)], Epoch);
        }

        // 最后一次进入 = 第 3 条事件；重放与增量必须一致（D-0010：恢复不重算）。
        Assert.Equal(3L, recovered.SlotEntrySequence);
        Assert.Equal(recovered.SlotEntrySequence, incremental.SlotEntrySequence);
        Assert.Equal(Epoch, recovered.SlotStartedAt);
        Assert.Equal(Epoch, incremental.SlotStartedAt);

        incremental.Clear();
        Assert.Null(incremental.SlotEntrySequence);
        Assert.Null(incremental.SlotStartedAt);
    }

    private static PhaseStartedEvent Night(GamePhase phase) => new()
    {
        Plan = new StepPlan { Label = $"test:{phase}", Phase = phase, Slots = [] },
        Control = ControlMode.Automatic,
    };

    private static SlotEnteredEvent SlotEntered(int index, string slotId) => new()
    {
        SlotIndex = index,
        SlotId = new StepSlotId(slotId),
    };

    private static StoredEventDraft Draft(long sequence, GameEvent gameEvent) => new()
    {
        Sequence = sequence,
        Event = gameEvent,
        RecordedAt = Epoch,
    };
}
