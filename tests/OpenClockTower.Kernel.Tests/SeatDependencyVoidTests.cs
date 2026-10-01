using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 行 8：座位依赖失效时**自动作废并给出原因**（票据第 5 条）——死亡、角色变更、无关变化；
/// 同时锁定"只观测到一个维度不误判另一个维度"（六维度相互独立）。
/// </summary>
public sealed class SeatDependencyVoidTests
{
    private static SeatDependency TargetDependency() => new()
    {
        Seat = new SeatId(2),
        RequiredLife = LifeState.Alive,
        RequiredCharacter = new CharacterId("clockmaker"),
    };

    private static StepMachineState StartWithTargetDependency()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, dependencies: [TargetDependency()]),
            StepFixture.Empty("empty-1"));
        return StepMachine.StartPhase(plan).State;
    }

    /// <summary>目标玩家死亡 → 自动作废，并把原因写进事件。</summary>
    [Fact]
    public void TargetDeath_AutoVoidsPendingRequest()
    {
        var state = StartWithTargetDependency();

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(2),
            Life = LifeState.Dead,
            Character = new CharacterId("clockmaker"),
            Reason = "测试：目标被杀",
            CausedBy = new SeatId(1),
        });

        Assert.Equal(StepMachineOutcomeKind.Applied, changed.Kind);
        var voided = Assert.IsType<OperationRequestVoid>(changed.State.PendingRequest!.Voided);
        Assert.Equal(OperationRequestVoidReason.DependencyViolated, voided.Reason);
        Assert.Equal(OperationRequestStatus.Voided, changed.State.PendingRequest.Status);
        Assert.Contains(changed.Events, e => e is OperationRequestVoidedEvent);

        var change = Assert.IsType<SeatStateChangedEvent>(
            Assert.Single(changed.Events, e => e is SeatStateChangedEvent));
        Assert.Equal("测试：目标被杀", change.Reason);
        Assert.Equal(new SeatId(1), change.CausedBy);
    }

    /// <summary>目标玩家角色变更 → 自动作废。</summary>
    [Fact]
    public void TargetCharacterChange_AutoVoidsPendingRequest()
    {
        var state = StartWithTargetDependency();

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(2),
            Life = LifeState.Alive,
            Character = new CharacterId("soldier"),
            Reason = "测试：角色被交换",
        });

        Assert.Equal(OperationRequestStatus.Voided, changed.State.PendingRequest!.Status);
        Assert.Equal(OperationRequestVoidReason.DependencyViolated, changed.State.PendingRequest.Voided!.Reason);
    }

    /// <summary>无关座位变化 → 请求照旧有效，但变化事实仍然落事件（说书人上帝视角）。</summary>
    [Fact]
    public void UnrelatedSeatChange_KeepsRequestPending_ButIsRecorded()
    {
        var state = StartWithTargetDependency();

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(3),
            Life = LifeState.Dead,
            Character = new CharacterId("soldier"),
            Reason = "测试：3 号被投毒",
            CausedBy = new SeatId(1),
        });

        Assert.Equal(OperationRequestStatus.Pending, changed.State.PendingRequest!.Status);
        Assert.Single(changed.Events, e => e is SeatStateChangedEvent);
        Assert.DoesNotContain(changed.Events, e => e is OperationRequestVoidedEvent);
    }

    /// <summary>依赖座位变化但事实不冲突（仍然存活、角色未变）→ 请求照旧有效。</summary>
    [Fact]
    public void NonViolatingChange_KeepsRequestPending()
    {
        var state = StartWithTargetDependency();

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(2),
            Life = LifeState.Alive,
            Character = new CharacterId("clockmaker"),
            Reason = "测试：状态未变",
        });

        Assert.Equal(OperationRequestStatus.Pending, changed.State.PendingRequest!.Status);
        Assert.DoesNotContain(changed.Events, e => e is OperationRequestVoidedEvent);
    }

    /// <summary>只观测到生死时，不得用未观测的角色去判"角色依赖"（避免误判）。</summary>
    [Fact]
    public void LifeOnlyObservation_DoesNotJudgeCharacterDependency()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action(
                "slot-1",
                1,
                dependencies: [new SeatDependency { Seat = new SeatId(1), RequiredCharacter = new CharacterId("clockmaker") }]),
            StepFixture.Empty("empty-1"));
        var state = StepMachine.StartPhase(plan).State;

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(1),
            Life = LifeState.Dead,
            Reason = "测试：只观测到生死",
        });

        Assert.Equal(OperationRequestStatus.Pending, changed.State.PendingRequest!.Status);
    }

    /// <summary>只观测到角色时，角色依赖仍应被正确判定。</summary>
    [Fact]
    public void CharacterOnlyObservation_VoidsCharacterDependency()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action(
                "slot-1",
                1,
                dependencies: [new SeatDependency { Seat = new SeatId(1), RequiredCharacter = new CharacterId("clockmaker") }]),
            StepFixture.Empty("empty-1"));
        var state = StepMachine.StartPhase(plan).State;

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(1),
            Character = new CharacterId("soldier"),
            Reason = "测试：角色变更",
        });

        Assert.Equal(OperationRequestStatus.Voided, changed.State.PendingRequest!.Status);
    }

    /// <summary>配额已走完时依赖失效 → 自动作废并立即推进（作废不额外等待）。</summary>
    [Fact]
    public void AutoVoidAfterQuotaElapsed_AdvancesImmediately()
    {
        var state = StartWithTargetDependency();
        var quotaElapsed = StepMachine.Handle(state, new SlotQuotaElapsedInput());
        Assert.Equal(0, quotaElapsed.State.SlotIndex);

        var changed = StepMachine.Handle(quotaElapsed.State, new SeatStateChangedInput
        {
            Seat = new SeatId(2),
            Life = LifeState.Dead,
            Character = new CharacterId("clockmaker"),
            Reason = "测试：配额走完后目标死亡",
        });

        Assert.Equal(1, changed.State.SlotIndex);
    }

    /// <summary>行动者自身死亡（计划构造方声明了该依赖）→ 自动作废。</summary>
    [Fact]
    public void AddresseeDeath_AutoVoidsWhenDeclared()
    {
        var plan = StepFixture.Plan(
            "test:night",
            StepFixture.Action("slot-1", 1, dependencies: [StepFixture.Alive(new SeatId(1))]),
            StepFixture.Empty("empty-1"));
        var state = StepMachine.StartPhase(plan).State;

        var changed = StepMachine.Handle(state, new SeatStateChangedInput
        {
            Seat = new SeatId(1),
            Life = LifeState.Dead,
            Reason = "测试：行动者死亡",
        });

        Assert.Equal(OperationRequestStatus.Voided, changed.State.PendingRequest!.Status);
    }
}
