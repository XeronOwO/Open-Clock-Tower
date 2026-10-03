using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 阻塞报警的解除原语（票据 terminal-hold-residue；R-0024 / D-0010 / D-0014）：
/// <see cref="SlotUnblockedEvent"/> 只清阻塞——位置、配额与其余挂起一个都不动；
/// 没有阻塞、槽位对不上、还没有阶段时一律显式失败；状态账折叠保持无操作。
/// </summary>
public sealed class SlotUnblockTests
{
    /// <summary>解除只清阻塞：除 <see cref="StepMachineState.Block"/> 外一个字段都不变。</summary>
    [Fact]
    public void Unblock_ClearsOnlyTheBlock()
    {
        var blocked = Blocked().State!;
        Assert.True(blocked.IsHeld);

        var unblocked = StepMachine.Apply(blocked, Unblock(blocked));

        Assert.NotNull(unblocked);
        Assert.Null(unblocked!.Block);
        Assert.False(unblocked.IsHeld);

        // 与"只把 Block 换掉"的状态相等：槽位下标、配额、挂起请求、裁定点、白天账、胜负结论全部原样。
        Assert.Equal(blocked with { Block = null }, unblocked);
    }

    /// <summary>事件里的槽位与当前槽位对不上 → 事件流损坏，显式抛错。</summary>
    [Fact]
    public void Unblock_WithMismatchedSlot_Throws()
    {
        var blocked = Blocked().State!;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            blocked,
            new SlotUnblockedEvent
            {
                SlotId = new StepSlotId("not-the-blocked-slot"),
                Reason = "测试：槽位对不上",
            }));
    }

    /// <summary>没有阻塞却要解除 → 事件流损坏，显式抛错（不静默清空气）。</summary>
    [Fact]
    public void Unblock_WithoutBlock_Throws()
    {
        var started = StepMachine.StartPhase(
            StepFixture.Plan("sv:night-2", StepFixture.Action("vortox", seat: 1, owner: "vortox")));

        Assert.Null(started.State!.Block);
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            started.State,
            new SlotUnblockedEvent
            {
                SlotId = started.State.CurrentSlot!.Id,
                Reason = "测试：本来就没有阻塞",
            }));
    }

    /// <summary>还没有任何阶段就要解除 → 顺序损坏，显式抛错。</summary>
    [Fact]
    public void Unblock_WithoutPhase_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            null,
            new SlotUnblockedEvent
            {
                SlotId = new StepSlotId("test-seat-1"),
                Reason = "测试：还没有阶段",
            }));
    }

    /// <summary>
    /// 状态账折叠保持无操作：它改的是步骤机状态，不进六维度与效果；
    /// 登记在案才不会被当成"未知事件类型"拒绝（恢复链路的第二个折叠器）。
    /// </summary>
    [Fact]
    public void Unblock_IsLedgerNoOp()
    {
        var unblocked = new SlotUnblockedEvent
        {
            SlotId = new StepSlotId("test-seat-1"),
            Reason = "本局已结束：阻塞报警不再有意义",
        };

        Assert.Equal(GameState.Empty, GameStateMachine.Apply(GameState.Empty, unblocked));
    }

    /// <summary>
    /// 造一个**真实**的阻塞态：空槽位却绑着一名存活持有者、而这一格没有行动契约——
    /// 与生产置位点同一条路径（<see cref="StepSlotEntry"/> 的 <c>OrphanReason</c>）。
    /// </summary>
    private static StepMachineOutcome Blocked()
    {
        var plan = StepFixture.Plan(
            "sv:night-2",
            StepSlot.Empty(new StepSlotId("sage"), new CharacterId("sage")));
        var outcome = StepMachine.StartPhase(plan, previous: null, Ledger((1, "sage", LifeState.Alive)));

        Assert.Contains(outcome.Events, gameEvent => gameEvent is SlotBlockedEvent);
        Assert.NotNull(outcome.State!.Block);
        return outcome;
    }

    private static SlotUnblockedEvent Unblock(StepMachineState state) => new()
    {
        SlotId = state.CurrentSlot!.Id,
        Reason = "本局已结束：阻塞报警不再有意义",
    };

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
