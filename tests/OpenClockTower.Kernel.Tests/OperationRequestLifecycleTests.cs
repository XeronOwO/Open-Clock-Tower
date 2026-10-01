using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 操作请求的生命周期：代填、作废、无合法选项三条路径（R-0009）与配额关系（D-0013）。
/// </summary>
public sealed class OperationRequestLifecycleTests
{
    /// <summary>行 6：说书人代填 → 事件里标明来源是代填，并保留缘由。</summary>
    [Fact]
    public void ProxyFill_IsRecordedWithStorytellerSource()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var filled = StepMachine.Handle(started.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-b",
            Source = ResponseSource.StorytellerProxy,
            Note = "玩家掉线，说书人代填",
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, filled.Kind);
        var answeredEvent = Assert.IsType<OperationRequestAnsweredEvent>(
            Assert.Single(filled.Events, e => e is OperationRequestAnsweredEvent));
        Assert.Equal(ResponseSource.StorytellerProxy, answeredEvent.Answer.Source);
        Assert.Equal("玩家掉线，说书人代填", answeredEvent.Answer.Note);
        Assert.Equal(OperationRequestStatus.Answered, filled.State.PendingRequest!.Status);
    }

    /// <summary>行 5：说书人强制作废 → 原因留痕；行 15：作废不缩短当前槽位配额。</summary>
    [Fact]
    public void ForceVoid_RecordsReason_AndDoesNotShortenQuota()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var voided = StepMachine.Handle(started.State, new VoidRequestInput
        {
            RequestId = requestId,
            Reason = OperationRequestVoidReason.StorytellerForce,
            Note = "玩家确认无法操作",
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, voided.Kind);
        Assert.Equal(OperationRequestStatus.Voided, voided.State.PendingRequest!.Status);
        Assert.Equal(OperationRequestVoidReason.StorytellerForce, voided.State.PendingRequest.Voided!.Reason);
        Assert.Equal(0, voided.State.SlotIndex);
        Assert.DoesNotContain(voided.Events, e => e is SlotAdvancedEvent);

        var afterQuota = StepMachine.Handle(voided.State, new SlotQuotaElapsedInput());
        Assert.Equal(1, afterQuota.State.SlotIndex);
    }

    /// <summary>配额已走完时作废 → 立即推进（作废本身不缩短，但槽位已到可推进条件）。</summary>
    [Fact]
    public void VoidAfterQuotaElapsed_AdvancesImmediately()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;
        var quotaFirst = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());

        var voided = StepMachine.Handle(quotaFirst.State, new VoidRequestInput
        {
            RequestId = requestId,
            Reason = OperationRequestVoidReason.StorytellerForce,
        });

        Assert.Equal(1, voided.State.SlotIndex);
    }

    /// <summary>R-0009 Skip：不发请求，但配额照走。</summary>
    [Fact]
    public void NoOptionSkip_IssuesNoRequest_ButConsumesQuota()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, StepFixture.EmptyPrompt(NoOptionBehavior.Skip)),
            StepFixture.Empty("empty-1"));

        var started = StepMachine.StartPhase(plan);

        Assert.Null(started.State.PendingRequest);
        Assert.Null(started.State.Block);
        Assert.Contains(started.Events, e => e is PromptSkippedEvent { SlotId: { Value: "slot-1" } });

        var afterQuota = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        Assert.Equal(1, afterQuota.State.SlotIndex);
    }

    /// <summary>R-0009 StorytellerDecides：把同一个契约投影给说书人；了结后才按配额推进。</summary>
    [Fact]
    public void NoOptionStorytellerDecides_RaisesDecisionPoint_UntilStorytellerResolves()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, StepFixture.EmptyPrompt(NoOptionBehavior.StorytellerDecides)),
            StepFixture.Empty("empty-1"));

        var started = StepMachine.StartPhase(plan);

        var decision = Assert.IsType<DecisionPoint>(started.State.AwaitingDecision);
        Assert.Equal("test:night:slot-1:decision", decision.Id.Value);
        Assert.Contains(started.Events, e => e is DecisionPointRaisedEvent);

        var quotaElapsed = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaElapsed.State.SlotIndex);

        var resolved = StepMachine.Handle(quotaElapsed.State, new ResolveDecisionPointInput
        {
            DecisionPointId = decision.Id,
            Decision = "说书人自由决定",
            Note = "R-0009 路径",
        });

        Assert.Null(resolved.State.AwaitingDecision);
        Assert.Equal(1, resolved.State.SlotIndex);
    }

    /// <summary>R-0009 BlockAndAlert：阻塞报警但不抛异常，等说书人兜底。</summary>
    [Fact]
    public void NoOptionBlockAndAlert_BlocksWithoutThrowing()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, StepFixture.EmptyPrompt(NoOptionBehavior.BlockAndAlert)),
            StepFixture.Empty("empty-1"));

        var started = StepMachine.StartPhase(plan);

        var block = Assert.IsType<StepBlock>(started.State.Block);
        Assert.Equal("没有合法选项的测试用选择", block.Reason);
        Assert.Null(started.State.PendingRequest);

        var quotaElapsed = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaElapsed.State.SlotIndex);
        Assert.NotNull(quotaElapsed.State.Block);
    }
}
