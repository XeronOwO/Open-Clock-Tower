using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 槽位进入时按**当前账**求值的回归：说明为什么要这样做——
/// 说书人在恶魔行动前杀死了尚未唤醒的恶魔，这一格就不该再唤醒他；
/// 角色在夜里被换走同理；而运行中被创造出来的角色要被激活（R-0030 第 6 条）。
/// </summary>
public sealed class SlotEntryLedgerTests
{
    /// <summary>行动者在进入这一格之前已经死亡 → 跳过，且不产操作请求（配额照走）。</summary>
    [Fact]
    public void ActorDeadBeforeEntry_SkipsWithoutRequest()
    {
        var plan = StepFixture.Plan("sv:night-2", StepFixture.Action("vortox", seat: 1, owner: "vortox"));
        var ledger = Ledger((1, "vortox", LifeState.Dead));

        var outcome = StepMachine.StartPhase(plan, previous: null, ledger);

        Assert.Contains(outcome.Events, gameEvent => gameEvent is PromptSkippedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    /// <summary>行动者的角色在这一格之前被换走 → 跳过（该请求已经失去意义）。</summary>
    [Fact]
    public void ActorCharacterChangedBeforeEntry_SkipsWithoutRequest()
    {
        var plan = StepFixture.Plan("sv:night-2", StepFixture.Action("vortox", seat: 1, owner: "vortox"));
        var ledger = Ledger((1, "dreamer", LifeState.Alive));

        var outcome = StepMachine.StartPhase(plan, previous: null, ledger);

        Assert.Contains(outcome.Events, gameEvent => gameEvent is PromptSkippedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    /// <summary>账里还没有这一席（内核夹具 / 半初始化）→ 判定不了就不改变行为：照常发请求。</summary>
    [Fact]
    public void ActorNotObserved_KeepsIssuingRequest()
    {
        var plan = StepFixture.Plan("sv:night-2", StepFixture.Action("clockmaker", seat: 1, owner: "clockmaker"));

        var outcome = StepMachine.StartPhase(plan, previous: null, GameState.Empty);

        Assert.Contains(outcome.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is PromptSkippedEvent);
    }

    /// <summary>行动者存活且角色相符 → 照常发请求。</summary>
    [Fact]
    public void ActorHealthyAndMatching_IssuesRequest()
    {
        var plan = StepFixture.Plan("sv:night-2", StepFixture.Action("clockmaker", seat: 1, owner: "clockmaker"));
        var ledger = Ledger((1, "clockmaker", LifeState.Alive));

        var outcome = StepMachine.StartPhase(plan, previous: null, ledger);

        Assert.Contains(outcome.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    /// <summary>
    /// 空槽位却已经有人在场上（夜里被创造出来但没被激活，且这一格没有契约）→ 显式阻塞，
    /// 不静默跳过（与建表期 <c>plan.contract_missing</c> 同族）。
    /// </summary>
    [Fact]
    public void EmptySlotWithLivingHolder_Blocks()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepSlot.Empty(new StepSlotId("sage"), new CharacterId("sage")));
        var ledger = Ledger((1, "sage", LifeState.Alive));

        var outcome = StepMachine.StartPhase(plan, previous: null, ledger);

        Assert.Contains(outcome.Events, gameEvent => gameEvent is SlotBlockedEvent);
        Assert.DoesNotContain(outcome.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    /// <summary>激活尚未进入的槽位 → 那一格变成行动槽位，轮到它时会发请求。</summary>
    [Fact]
    public void SlotActivated_PendingSlot_BecomesActionAndIssuesRequest()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepFixture.Beat("dusk"),
            StepSlot.Empty(new StepSlotId("vortox"), new CharacterId("vortox")));

        var started = StepMachine.StartPhase(plan, previous: null, GameState.Empty);
        var activated = StepMachine.Apply(
            started.State,
            new SlotActivatedEvent
            {
                SlotIndex = 1,
                SlotId = new StepSlotId("vortox"),
                Actor = new SeatId(2),
                Prompt = StepFixture.Prompt("seat:1", "seat:2"),
            });

        Assert.NotNull(activated);
        Assert.Equal(StepSlotKind.Action, activated!.Plan.Slots[1].Kind);
        Assert.Equal(new SeatId(2), activated.Plan.Slots[1].Actor);
        Assert.Equal(new CharacterId("vortox"), activated.Plan.Slots[1].Owner);

        // 节拍槽位配额走完 → 推进到被激活的那一格：它现在会发请求。
        var advanced = StepMachine.Handle(activated, new SlotQuotaElapsedInput());
        Assert.Contains(advanced.Events, gameEvent => gameEvent is OperationRequestIssuedEvent);
    }

    /// <summary>激活一个已经进入过的槽位 = 事件流损坏 → 显式抛错（恢复必须失败，不静默继续）。</summary>
    [Fact]
    public void SlotActivated_ForEnteredSlot_Throws()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepSlot.Empty(new StepSlotId("vortox"), new CharacterId("vortox")));
        var started = StepMachine.StartPhase(plan, previous: null, GameState.Empty);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            started.State,
            new SlotActivatedEvent
            {
                SlotIndex = 0,
                SlotId = new StepSlotId("vortox"),
                Actor = new SeatId(1),
                Prompt = StepFixture.Prompt("seat:1"),
            }));
    }

    private static GameState Ledger(params (int Seat, string Character, LifeState Life)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Life = Fact(row.Life),
                    Alignment = Fact(Alignment.Good),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };
}
