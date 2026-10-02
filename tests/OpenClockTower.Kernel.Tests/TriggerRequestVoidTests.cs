using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 触发来源请求在**计划走完之后**的作废路径（胜负票独立复核 F-4）。
/// </summary>
/// <remarks>
/// 呆瓜的公开选择是触发来源的请求，R-0027 明确它「常常正好开在白天关闭（计划走完）之后、
/// 下一夜开始之前」；D-0011 / D-0014 要求说书人的兜底入口（代填 / 作废 / 强推）**永远开着**。
/// 作答路径（代填走 <c>HandleResponse</c>）有非槽位旁路，作废路径如果缺同款旁路，
/// 一条开在计划走完之后的请求就会既答得了、也撤不掉——强推同样被 <c>IsPlanCompleted</c> 挡住。
/// </remarks>
public sealed class TriggerRequestVoidTests
{
    private static readonly OperationRequestId TriggerRequestId = new("trigger:klutz:1");

    /// <summary>触发来源 + 计划已走完 → 说书人仍可作废（否则兜底入口关死）。</summary>
    [Fact]
    public void VoidTriggerRequest_AfterPlanCompleted_IsAccepted()
    {
        var state = CompletedPlanWith(OperationRequestOrigin.ForTrigger(
            new AbilityId("klutz.choice"),
            "呆瓜死亡后的公开选择（R-0027）"));

        var outcome = StepMachine.Handle(
            state,
            new VoidRequestInput
            {
                RequestId = TriggerRequestId,
                Reason = OperationRequestVoidReason.StorytellerForce,
                Note = "玩家一直不选，说书人跳过",
            });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(outcome.Events, gameEvent => gameEvent is OperationRequestVoidedEvent);
        Assert.Equal(OperationRequestStatus.Voided, outcome.State!.PendingRequest!.Status);
    }

    /// <summary>槽位来源的请求在计划走完之后仍然不可作废——它本来就该随槽位了结。</summary>
    [Fact]
    public void VoidSlotRequest_AfterPlanCompleted_IsStillRejected()
    {
        var state = CompletedPlanWith(OperationRequestOrigin.ForSlot(
            new StepSlotId("clockmaker"),
            "sv:night-2",
            slotIndex: 0));

        var outcome = StepMachine.Handle(
            state,
            new VoidRequestInput
            {
                RequestId = TriggerRequestId,
                Reason = OperationRequestVoidReason.StorytellerForce,
            });

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(StepMachineRejectionReason.PlanAlreadyCompleted, outcome.RejectionReason);
    }

    /// <summary>把"计划已走完 + 一条挂起请求"拼出来（请求来源由调用方指定）。</summary>
    private static StepMachineState CompletedPlanWith(OperationRequestOrigin origin)
    {
        var plan = StepFixture.Plan("sv:night-2", StepFixture.Beat("dusk"));
        var started = StepMachine.StartPhase(plan, previous: null, GameState.Empty);

        // 唯一槽位：配额走完 → 自动推进 → 计划完成。
        var completed = StepMachine.Handle(started.State!, new SlotQuotaElapsedInput());
        Assert.True(completed.State!.IsPlanCompleted);

        return completed.State with
        {
            PendingRequest = new OperationRequest
            {
                Id = TriggerRequestId,
                Addressee = new SeatId(1),
                Origin = origin,
                Prompt = StepFixture.Prompt("seat:1", "seat:2"),
            },
        };
    }
}
