using System.Text.Json;
using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 槽位追加原语（口径见 <c>docs/standard/rulings.md</c> R-0055）：非首个夜晚获得的「首个夜晚」能力
/// 在夜晚顺序表上没有位置，由结算契约**追加**一格；追加只许落在尚未进入的位置，
/// 顺序损坏一律显式抛错（D-0014 能力 3）。
/// </summary>
public sealed class SlotInsertionTests
{
    /// <summary>追加一格未来位置：插在原下标之前，当前槽位与下标都不受影响。</summary>
    [Fact]
    public void Insert_FutureSlot_PlacesItAndKeepsCurrentIndex()
    {
        var started = StepMachine.StartPhase(Plan());
        var state = started.State;
        Assert.Equal(0, state.SlotIndex);

        var inserted = StepMachine.Apply(state, Insert(index: 2))!;

        Assert.Equal(4, inserted.Plan.Slots.Count);
        Assert.Equal(new StepSlotId("clockmaker-added"), inserted.Plan.Slots[2].Id);
        Assert.Equal("clockmaker", inserted.Plan.Slots[2].Owner?.Value);
        Assert.Equal(new SeatId(3), inserted.Plan.Slots[2].Actor);
        Assert.Equal(0, inserted.SlotIndex);
        Assert.Equal(new StepSlotId("dreamer"), inserted.CurrentSlot!.Id);
    }

    /// <summary>紧跟当前槽位之后（下标 = 当前 + 1）是合法追加位。</summary>
    [Fact]
    public void Insert_RightAfterCurrentSlot_IsAllowed()
    {
        var state = StepMachine.StartPhase(Plan()).State;

        var inserted = StepMachine.Apply(state, Insert(index: 1))!;

        Assert.Equal(new StepSlotId("clockmaker-added"), inserted.Plan.Slots[1].Id);
        Assert.Equal(0, inserted.SlotIndex);
    }

    /// <summary>挂起中的请求不因为未来位置被追加而改变（重放与投影都不分叉）。</summary>
    [Fact]
    public void Insert_WhileSuspended_KeepsPendingRequestAndPass()
    {
        var state = StepMachine.StartPhase(Plan()).State;
        var history = state with { SlotPass = 1, SlotAbilityResolved = true };

        var inserted = StepMachine.Apply(history, Insert(index: 2))!;

        Assert.Equal(history.PendingRequest, inserted.PendingRequest);
        Assert.Equal(1, inserted.SlotPass);
        Assert.True(inserted.SlotAbilityResolved);
    }

    /// <summary>已经走过 / 正在走的位置不能回填槽位：显式抛错。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Insert_AtCurrentOrPastPosition_Throws(int index)
    {
        var state = StepMachine.StartPhase(Plan()).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(state, Insert(index)));
    }

    /// <summary>越界（超过计划格数）显式抛错。</summary>
    [Fact]
    public void Insert_OutOfRange_Throws()
    {
        var state = StepMachine.StartPhase(Plan()).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(state, Insert(index: 4)));
    }

    /// <summary>槽位标识不得与计划里已有的重复（复盘与请求标识都按它认人）。</summary>
    [Fact]
    public void Insert_DuplicateSlotId_Throws()
    {
        var state = StepMachine.StartPhase(Plan()).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            state,
            new SlotInsertedEvent
            {
                Index = 2,
                Slot = StepFixture.Action("dreamer", 3, owner: "clockmaker"),
            }));
    }

    /// <summary>只有角色行动格能被追加：节拍 / 黎明 / 空槽位一律拒绝。</summary>
    [Fact]
    public void Insert_NonActionSlot_Throws()
    {
        var state = StepMachine.StartPhase(Plan()).State;

        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(
            state,
            new SlotInsertedEvent { Index = 2, Slot = StepFixture.Beat("beat-added") }));
    }

    /// <summary>还没有任何阶段时追加 → 事件流顺序损坏。</summary>
    [Fact]
    public void Insert_WithoutPhase_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => StepMachine.Apply(null, Insert(index: 1)));
    }

    /// <summary>折叠（重放）得到同一份含追加格的计划。</summary>
    [Fact]
    public void Fold_Replay_ReproducesInsertedPlan()
    {
        var started = StepMachine.StartPhase(Plan());
        var inserted = StepMachine.Apply(started.State, Insert(index: 2))!;

        var folded = StepMachine.Fold([.. started.Events, Insert(index: 2)]);

        Assert.NotNull(folded);
        Assert.True(StepMachineStateComparer.AreEquivalent(inserted, folded));
        Assert.Equal(new StepSlotId("clockmaker-added"), folded!.Plan.Slots[2].Id);
    }

    /// <summary>追加格随快照 JSON 往返（提示是数据，计划要能原样存回来；Owner / Character 是关键字段：
    /// 前者是结算契约的检索键、后者是入格确认行动者还站得住的依据）。</summary>
    [Fact]
    public void InsertedSlot_SurvivesJsonRoundTrip()
    {
        var state = StepMachine.Apply(StepMachine.StartPhase(Plan()).State, Insert(index: 2))!;

        var restored = JsonSerializer.Deserialize<StepMachineState>(JsonSerializer.Serialize(state));

        Assert.True(StepMachineStateComparer.AreEquivalent(state, restored));
        Assert.Equal(new StepSlotId("clockmaker-added"), restored!.Plan.Slots[2].Id);
        Assert.Equal(new CharacterId("clockmaker"), restored.Plan.Slots[2].Owner);
        Assert.Equal(new CharacterId("clockmaker"), state.Plan.Slots[2].Character);
        Assert.Equal(new SeatId(3), restored.Plan.Slots[2].Actor);
        Assert.True(restored.Plan.Slots[2].Prompt!.HasOptions);
    }

    private static SlotInsertedEvent Insert(int index) => new()
    {
        Index = index,
        Slot = StepFixture.Action(
            "clockmaker-added",
            3,
            dependencies: [StepFixture.Alive(new SeatId(3))],
            owner: "clockmaker"),
    };

    private static StepPlan Plan() => StepFixture.Plan(
        "sv:night-3",
        StepFixture.Action("dreamer", 1, owner: "dreamer"),
        StepFixture.Empty("empty-1"),
        StepFixture.DawnWait("dawn"));
}
