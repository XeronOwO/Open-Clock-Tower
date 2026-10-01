using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 步骤机主行为：推进条件（配额 + 挂起了结）、空槽位配额、强推与接管（D-0013 / D-0014）。
/// </summary>
public sealed class StepMachineTests
{
    /// <summary>进入行动槽位 → 立刻向行动者发出操作请求。</summary>
    [Fact]
    public void StartPhase_EntersFirstSlot_AndIssuesRequestForActor()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1, dependencies: [StepFixture.Alive(new SeatId(1))]));

        var outcome = StepMachine.StartPhase(plan);

        Assert.Equal(StepMachineOutcomeKind.Applied, outcome.Kind);
        Assert.Equal(0, outcome.State.SlotIndex);
        var request = Assert.IsType<OperationRequest>(outcome.State.PendingRequest);
        Assert.Equal(new SeatId(1), request.Addressee);
        Assert.Equal(OperationRequestStatus.Pending, request.Status);
        Assert.Equal("test:night:slot-1", request.Id.Value);
        Assert.Contains(outcome.Events, e => e is OperationRequestIssuedEvent);
        Assert.Contains(outcome.Events, e => e is SlotEnteredEvent);
    }

    /// <summary>空槽位没有请求，但配额照走；配额走完才推进（D-0013 §1）。</summary>
    [Fact]
    public void EmptySlot_ConsumesQuota_AndAdvancesAfterQuotaElapsed()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Empty("empty-1"), StepFixture.Empty("empty-2"));
        var started = StepMachine.StartPhase(plan);

        Assert.Null(started.State.PendingRequest);
        Assert.Equal(0, started.State.SlotIndex);

        var advanced = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());

        Assert.Equal(1, advanced.State.SlotIndex);
        Assert.Contains(advanced.Events, e => e is SlotAdvancedEvent { FromIndex: 0, ToIndex: 1 });
        Assert.Contains(advanced.Events, e => e is SlotEnteredEvent { SlotIndex: 1 });
    }

    /// <summary>节拍槽位（黄昏 / 信息环节）没有请求，配额照走；与空槽位同样是纯时间配额。</summary>
    [Fact]
    public void BeatSlot_ConsumesQuota_AndIssuesNoRequest()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Beat("beat-1"), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);

        Assert.Null(started.State.PendingRequest);
        Assert.Equal(StepSlotKind.Beat, started.State.CurrentSlot!.Kind);

        var advanced = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());

        Assert.Equal(1, advanced.State.SlotIndex);
        Assert.Contains(advanced.Events, e => e is SlotAdvancedEvent { FromIndex: 0, ToIndex: 1 });
        Assert.Contains(advanced.Events, e => e is SlotEnteredEvent { SlotIndex: 1 });
    }

    /// <summary>秒回不提前推进：响应只把挂起解除，仍要等配额走完（D-0013 §4）。</summary>
    [Fact]
    public void ResponseDoesNotAdvanceBeforeQuotaElapsed()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var answered = StepMachine.Handle(started.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-a",
            Source = ResponseSource.Player,
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, answered.Kind);
        Assert.Equal(0, answered.State.SlotIndex);
        Assert.Equal(OperationRequestStatus.Answered, answered.State.PendingRequest!.Status);
        Assert.DoesNotContain(answered.Events, e => e is SlotAdvancedEvent);

        var afterQuota = StepMachine.Handle(answered.State, new SlotQuotaElapsedInput());

        Assert.Equal(1, afterQuota.State.SlotIndex);
        Assert.Null(afterQuota.State.PendingRequest);
    }

    /// <summary>配额先走完、响应后到：响应一到立即推进（两个条件都已满足）。</summary>
    [Fact]
    public void ResponseAfterQuotaElapsed_AdvancesImmediately()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var quotaFirst = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaFirst.State.SlotIndex);
        Assert.Equal(SlotQuotaState.Elapsed, quotaFirst.State.Quota);

        var answered = StepMachine.Handle(quotaFirst.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-b",
            Source = ResponseSource.Player,
        });

        Assert.Equal(1, answered.State.SlotIndex);
    }

    /// <summary>行 3：请求一直有效，不自动过期、不自动跳过——配额走完也不推进。</summary>
    [Fact]
    public void PendingRequest_IsNeverSkippedByQuota()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);

        var firstQuota = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        var repeatedQuota = StepMachine.Handle(firstQuota.State, new SlotQuotaElapsedInput());

        Assert.Equal(0, repeatedQuota.State.SlotIndex);
        Assert.Equal(OperationRequestStatus.Pending, repeatedQuota.State.PendingRequest!.Status);
        Assert.DoesNotContain(repeatedQuota.Events, e => e is SlotAdvancedEvent);
    }

    /// <summary>非法响应：未知请求 / 非法选项 / 重复响应都被拒绝，状态不变。</summary>
    [Fact]
    public void SubmitResponse_RejectsUnknownRequestIllegalOptionAndDoubleAnswer()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var unknownRequest = StepMachine.Handle(started.State, new SubmitResponseInput
        {
            RequestId = new OperationRequestId("test:night:not-a-slot"),
            OptionValue = "option-a",
            Source = ResponseSource.Player,
        });
        Assert.Equal(StepMachineRejectionReason.NotCurrentRequest, unknownRequest.RejectionReason);
        Assert.Empty(unknownRequest.Events);

        var illegalOption = StepMachine.Handle(started.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-not-listed",
            Source = ResponseSource.Player,
        });
        Assert.Equal(StepMachineRejectionReason.OptionNotLegal, illegalOption.RejectionReason);
        Assert.Equal(OperationRequestStatus.Pending, illegalOption.State.PendingRequest!.Status);

        var answered = StepMachine.Handle(started.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-a",
            Source = ResponseSource.Player,
        });
        var doubleAnswer = StepMachine.Handle(answered.State, new SubmitResponseInput
        {
            RequestId = requestId,
            OptionValue = "option-b",
            Source = ResponseSource.Player,
        });
        Assert.Equal(StepMachineRejectionReason.RequestAlreadyResolved, doubleAnswer.RejectionReason);
        Assert.Empty(doubleAnswer.Events);
    }

    /// <summary>最后一个槽位走完 → 产出 PhaseCompleted，计划结束。</summary>
    [Fact]
    public void LastSlot_CompletesPlan()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);

        var completed = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());

        Assert.True(completed.State.IsPlanCompleted);
        Assert.Equal(1, completed.State.SlotIndex);
        Assert.Contains(completed.Events, e => e is PhaseCompletedEvent { PlanLabel: "test:night" });
    }

    /// <summary>行 21：强推越过配额与挂起，请求按 Override 原因了结，事件留痕（D-0014）。</summary>
    [Fact]
    public void ForceAdvance_SkipsQuotaAndVoidsPendingRequest()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Action("slot-1", 1), StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var requestId = started.State.PendingRequest!.Id;

        var forced = StepMachine.Handle(started.State, new ForceAdvanceInput { Reason = "程序逻辑出错，兜底继续" });

        Assert.Equal(StepMachineOutcomeKind.Applied, forced.Kind);
        Assert.Equal(1, forced.State.SlotIndex);
        Assert.Null(forced.State.PendingRequest);
        Assert.Contains(
            forced.Events,
            e => e is SlotForceAdvancedEvent { Reason: "程序逻辑出错，兜底继续" });
        var voidEvent = Assert.IsType<OperationRequestVoidedEvent>(
            Assert.Single(forced.Events, e => e is OperationRequestVoidedEvent));
        Assert.Equal(requestId, voidEvent.RequestId);
        Assert.Equal(OperationRequestVoidReason.StorytellerTakeover, voidEvent.Void.Reason);
    }

    /// <summary>行 22：接管暂停自动推进，交还后恢复（D-0014）。</summary>
    [Fact]
    public void TakeOver_PausesAutoAdvance_AndReleaseResumes()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Empty("empty-1"), StepFixture.Empty("empty-2"));
        var started = StepMachine.StartPhase(plan);

        var takeover = StepMachine.Handle(started.State, new TakeOverInput { Reason = "说书人手动调板" });
        Assert.Equal(ControlMode.StorytellerTakeover, takeover.State.Control);

        var quotaElapsed = StepMachine.Handle(takeover.State, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaElapsed.State.SlotIndex);
        Assert.Equal(SlotQuotaState.Elapsed, quotaElapsed.State.Quota);

        var release = StepMachine.Handle(quotaElapsed.State, new ReleaseControlInput { Reason = "交还自动化" });
        Assert.Equal(ControlMode.Automatic, release.State.Control);
        Assert.Equal(1, release.State.SlotIndex);
    }

    /// <summary>重复接管被拒绝：控制模式本来就是接管。</summary>
    [Fact]
    public void TakeOver_WhenAlreadyTakenOver_IsRejected()
    {
        var plan = StepFixture.Plan("test:night", StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);
        var takeover = StepMachine.Handle(started.State, new TakeOverInput { Reason = "接管" });

        var again = StepMachine.Handle(takeover.State, new TakeOverInput { Reason = "再接管" });

        Assert.Equal(StepMachineOutcomeKind.Rejected, again.Kind);
        Assert.Equal(StepMachineRejectionReason.ControlModeUnchanged, again.RejectionReason);
    }

    /// <summary>行 21：阻塞状态下兜底入口可用——强推解阻塞并继续。</summary>
    [Fact]
    public void ForceAdvance_ClearsBlockedSlot()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, StepFixture.EmptyPrompt(NoOptionBehavior.BlockAndAlert)),
            StepFixture.Empty("empty-1"));
        var started = StepMachine.StartPhase(plan);

        Assert.NotNull(started.State.Block);
        Assert.Null(started.State.PendingRequest);

        var quotaElapsed = StepMachine.Handle(started.State, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaElapsed.State.SlotIndex);

        var forced = StepMachine.Handle(quotaElapsed.State, new ForceAdvanceInput { Reason = "人工处理阻塞" });

        Assert.Equal(1, forced.State.SlotIndex);
        Assert.Null(forced.State.Block);
    }
}
