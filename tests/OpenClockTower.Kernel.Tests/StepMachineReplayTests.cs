using System.Text.Json;
using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 步骤机的可重放性：Handle 产事件、Apply 折叠回状态；挂起状态可 JSON 往返（D-0011 硬约束 4 / D-0014 能力 3）。
/// </summary>
public sealed class StepMachineReplayTests
{
    /// <summary>跑一段包含"响应 + 空槽位 + 黎明等待"的真实迁移，返回终态与全部事件。</summary>
    private static (StepMachineState State, List<GameEvent> Events) RunScript()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1),
            StepFixture.Empty("empty-1"),
            StepFixture.DawnWait("dawn"));
        var events = new List<GameEvent>();

        var started = StepMachine.StartPhase(plan);
        events.AddRange(started.Events);
        var state = started.State;

        var quotaWhileHeld = StepMachine.Handle(state, new SlotQuotaElapsedInput());
        events.AddRange(quotaWhileHeld.Events);
        state = quotaWhileHeld.State;
        Assert.Equal(0, state.SlotIndex);

        var answered = StepMachine.Handle(state, new SubmitResponseInput
        {
            RequestId = state.PendingRequest!.Id,
            OptionValue = "option-a",
            Source = ResponseSource.Player,
        });
        events.AddRange(answered.Events);
        state = answered.State;
        Assert.Equal(1, state.SlotIndex);

        var emptyQuota = StepMachine.Handle(state, new SlotQuotaElapsedInput());
        events.AddRange(emptyQuota.Events);
        state = emptyQuota.State;
        Assert.Equal(2, state.SlotIndex);

        var dawnQuota = StepMachine.Handle(state, new SlotQuotaElapsedInput());
        events.AddRange(dawnQuota.Events);
        state = dawnQuota.State;

        return (state, events);
    }

    /// <summary>折叠全部事件应精确重建终态（重放可信）。</summary>
    [Fact]
    public void Fold_OfEmittedEvents_ReproducesFinalState()
    {
        var (state, events) = RunScript();

        var folded = StepMachine.Fold(events);

        Assert.True(StepMachineStateComparer.AreEquivalent(state, folded));
        Assert.True(folded.IsPlanCompleted);
        Assert.Contains(events, e => e is PhaseCompletedEvent);
    }

    /// <summary>行 7 的基础：挂起中的状态可序列化 / 反序列化后逐字段一致。</summary>
    [Fact]
    public void PendingState_SurvivesJsonRoundTrip()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, dependencies: [StepFixture.Alive(new SeatId(1))]),
            StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);

        var json = JsonSerializer.Serialize(started.State);
        var restored = JsonSerializer.Deserialize<StepMachineState>(json);

        Assert.True(StepMachineStateComparer.AreEquivalent(started.State, restored));
        Assert.Equal(OperationRequestStatus.Pending, restored!.PendingRequest!.Status);
        Assert.Equal("test:night:slot-1", restored.PendingRequest.Id.Value);
    }

    /// <summary>事件流损坏 → **显式失败**，不得静默产出半个状态（D-0014 能力 3）。</summary>
    [Fact]
    public void CorruptEventStream_FailsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(null, new SlotEnteredEvent
        {
            SlotIndex = 0,
            SlotId = new StepSlotId("slot-1"),
        }));
    }
}
