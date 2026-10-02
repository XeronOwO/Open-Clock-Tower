using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 白天强推越过挂起请求时必须产出可审计的作废事件（D-0014 / R-0027 第 4 条）。
/// </summary>
/// <remarks>
/// 独立对抗性复核 F-1：槽位推进会把 <c>PendingRequest</c> 置空，若强推不补作废事件，
/// 请求就是"无声消失"——触发型能力看不出自己已被越过，会在下一个黎明重复开选择，
/// 终局后果可以被反复重掷。
/// </remarks>
public sealed class DayForceAdvanceTests
{
    [Fact]
    public void ForceAdvance_Day_WithPendingRequest_EmitsVoidEvent()
    {
        var plan = StepFixture.Plan("test:day-1", StepFixture.Empty("day-slot")) with { Phase = GamePhase.Day };
        var started = StepMachine.StartPhase(plan).State;
        var day = StepMachine.Apply(started, new DayStartedEvent { DayNumber = 1 })!;
        var request = new OperationRequest
        {
            Id = new OperationRequestId("klutz:2"),
            Addressee = new SeatId(2),
            Origin = OperationRequestOrigin.ForTrigger(new AbilityId("klutz.choice"), "测试：呆瓜死亡选择"),
            Prompt = new ChoicePrompt
            {
                Context = "测试：呆瓜选择",
                Options = [new DecisionOption { Value = "seat:1", Preview = "1 号玩家" }],
                OnNoOption = NoOptionBehavior.BlockAndAlert,
            },
        };
        var withRequest = day with { PendingRequest = request };

        var outcome = StepMachine.Handle(withRequest, new ForceAdvanceInput { Reason = "测试：说书人越过白天" });

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        var voided = Assert.Single(outcome.Events.OfType<OperationRequestVoidedEvent>());
        Assert.Equal(request.Id, voided.RequestId);
        Assert.Equal(OperationRequestVoidReason.StorytellerForce, voided.Void.Reason);
        Assert.Null(outcome.State.PendingRequest);
        Assert.Contains(outcome.Events, item => item is DayClosedEvent);
    }
}
