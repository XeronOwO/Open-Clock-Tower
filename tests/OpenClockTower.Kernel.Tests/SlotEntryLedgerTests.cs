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

    /// <summary>
    /// 追加格（R-0055）轮到一名**已死亡**的行动者：与集骨者那条放行判据同源——
    /// 只有「重获能力」窗口仍在时才放行，否则显式跳过（不静默）。
    /// </summary>
    [Fact]
    public void AppendedEntrySlot_DeadActorWithoutRegainWindow_Skips()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepFixture.Action("clockmaker@2", seat: 2, owner: "clockmaker"));
        var ledger = Ledger((2, "clockmaker", LifeState.Dead));

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

    /// <summary>
    /// 角色在夜里换手（舞蛇人交换等）→ 尚未进入的行动槽位重绑到新持有者，
    /// 轮到它时向新持有者发请求（rulings.md R-0032）。
    /// </summary>
    [Fact]
    public void SlotRebound_ToNewHolder_IssuesRequestToNewActor()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepFixture.Beat("dusk"),
            StepFixture.Action("vortox", seat: 1, owner: "vortox"));

        var started = StepMachine.StartPhase(plan, previous: null, GameState.Empty);
        var rebound = StepMachine.Apply(
            started.State,
            new SlotActivatedEvent
            {
                SlotIndex = 1,
                SlotId = new StepSlotId("vortox"),
                Actor = new SeatId(2),
                Prompt = StepFixture.Prompt("seat:2"),
            });

        Assert.NotNull(rebound);
        Assert.Equal(StepSlotKind.Action, rebound!.Plan.Slots[1].Kind);
        Assert.Equal(new SeatId(2), rebound.Plan.Slots[1].Actor);
        Assert.Equal(new CharacterId("vortox"), rebound.Plan.Slots[1].Owner);

        // 节拍槽位配额走完 → 推进到被重绑的那一格：请求发给新持有者。
        var advanced = StepMachine.Handle(rebound, new SlotQuotaElapsedInput());
        var issued = Assert.Single(advanced.Events.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(new SeatId(2), issued.Request.Addressee);
    }

    /// <summary>行动槽位重复绑定同一行动者 = 事件流损坏 → 显式抛错（严格性不放松）。</summary>
    [Fact]
    public void SlotRebound_ToSameActor_Throws()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepFixture.Beat("dusk"),
            StepFixture.Action("vortox", seat: 1, owner: "vortox"));
        var started = StepMachine.StartPhase(plan, previous: null, GameState.Empty);

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            started.State,
            new SlotActivatedEvent
            {
                SlotIndex = 1,
                SlotId = new StepSlotId("vortox"),
                Actor = new SeatId(1),
                Prompt = StepFixture.Prompt("seat:1"),
            }));
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

    /// <summary>
    /// 哲学家代行被获得角色的能力（R-0036）：激活带上行动者**本人**的角色时，那一格重建为代行槽位——
    /// 能力契约仍取槽位角色（`Owner`），进入时按行动者本人的角色确认（`Character`），因此不会被跳过。
    /// </summary>
    [Fact]
    public void SlotActivated_WithActorCharacter_RebuildsGrantedSlot()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepFixture.Beat("dusk"),
            StepSlot.Empty(new StepSlotId("dreamer"), new CharacterId("dreamer")));
        var ledger = Ledger((1, "philosopher", LifeState.Alive), (2, "klutz", LifeState.Alive));

        var started = StepMachine.StartPhase(plan, previous: null, ledger);
        var activated = StepMachine.Apply(
            started.State,
            new SlotActivatedEvent
            {
                SlotIndex = 1,
                SlotId = new StepSlotId("dreamer"),
                Actor = new SeatId(1),
                ActorCharacter = new CharacterId("philosopher"),
                Prompt = StepFixture.Prompt("seat:2"),
                Dependencies =
                [
                    new SeatDependency
                    {
                        Seat = new SeatId(1),
                        RequiredLife = LifeState.Alive,
                        RequiredCharacter = new CharacterId("philosopher"),
                    },
                ],
            })!;

        var slot = activated.Plan.Slots[1];
        Assert.Equal(StepSlotKind.Action, slot.Kind);
        Assert.Equal(new SeatId(1), slot.Actor);
        Assert.Equal(new CharacterId("dreamer"), slot.Owner);
        Assert.Equal(new CharacterId("philosopher"), slot.Character);

        // 进入那一格：行动者（哲学家）存活且角色相符 → 请求发给他；被获得角色的"位置"不影响唤醒判定。
        var advanced = StepMachine.Handle(activated, new SlotQuotaElapsedInput());
        var issued = Assert.Single(advanced.Events.OfType<OperationRequestIssuedEvent>());
        Assert.Equal(new SeatId(1), issued.Request.Addressee);
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
