using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 能力结算结论折进状态账的两本账（架构 §2.2），以及「维度 → 效果链接」的落点。
/// </summary>
public sealed class AbilityLedgerTests
{
    [Fact]
    public void EffectiveResolution_RecordsUseOnly()
    {
        var state = GameStateMachine.Fold(
        [
            Resolved(effective: true),
        ]);

        var use = Assert.Single(state.AbilityUses.Entries);
        Assert.Equal(new SeatId(2), use.Seat);
        Assert.Equal(new AbilityId("dreamer"), use.Ability);
        Assert.True(use.Effective);
        Assert.True(state.AbilityUses.WasUsed(new SeatId(2), new AbilityId("dreamer")));
        Assert.True(state.AbilityUses.WasEffective(new SeatId(2), new AbilityId("dreamer")));
        Assert.Empty(state.Malfunctions.Entries);
    }

    [Fact]
    public void IneffectiveResolution_RecordsUseAndMalfunction()
    {
        var state = GameStateMachine.Fold(
        [
            Resolved(effective: false, MalfunctionKind.Poisoned),
        ]);

        var use = Assert.Single(state.AbilityUses.Entries);
        Assert.False(use.Effective);
        Assert.False(state.AbilityUses.WasEffective(new SeatId(2), new AbilityId("dreamer")));
        var malfunction = Assert.Single(state.Malfunctions.Entries);
        Assert.Equal(MalfunctionKind.Poisoned, malfunction.Kind);
        Assert.Equal(1, state.Malfunctions.Count);
        Assert.Empty(state.Malfunctions.Unclassified);
    }

    [Fact]
    public void UnclassifiedKind_LeavesOpenEntryOnTheChecklist()
    {
        var state = GameStateMachine.Fold(
        [
            Resolved(effective: false, MalfunctionKind.Open),
        ]);

        var unclassified = Assert.Single(state.Malfunctions.Unclassified);
        Assert.Equal(MalfunctionKind.Open, unclassified.Kind);
    }

    /// <summary>状态变化事件带 EffectId 时，事实里保留链接（面板据此追问"是哪条效果"）。</summary>
    [Fact]
    public void SeatStateChange_KeepsEffectLink()
    {
        var effectId = new EffectId("standing:no-dashii.poison:1:2");
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Poison = PoisonState.Poisoned,
                Reason = "诺-达鲺的常驻中毒",
                CausedBy = new SeatId(1),
                EffectId = effectId,
            },
        ]);

        var entry = state.Seat(new SeatId(2));
        Assert.NotNull(entry);
        Assert.Equal(PoisonState.Poisoned, entry!.PoisonValue);
        Assert.Equal(effectId, entry.Poison!.EffectId);
        Assert.Equal(new SeatId(1), entry.Poison.CausedBy);
    }

    /// <summary>同一批事件逐条折叠与一次性折叠必须一致（D-0008：重放可信）。</summary>
    [Fact]
    public void FoldMatchesIncrementalApply()
    {
        GameEvent[] events =
        [
            Resolved(effective: true),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Drunk = DrunkState.Drunk,
                Reason = "测试上报",
                CausedBy = new SeatId(3),
            },
            Resolved(effective: false, MalfunctionKind.Drunk),
        ];

        var folded = GameStateMachine.Fold(events);
        var incremental = GameState.Empty;
        foreach (var gameEvent in events)
        {
            incremental = GameStateMachine.Apply(incremental, gameEvent);
        }

        Assert.Equal(folded.AbilityUses.Entries.Count, incremental.AbilityUses.Entries.Count);
        Assert.Equal(folded.Malfunctions.Entries.Count, incremental.Malfunctions.Entries.Count);
        Assert.Equal(folded.Malfunctions.Unclassified.Count, incremental.Malfunctions.Unclassified.Count);
    }

    /// <summary>R-0004：一次结算命中多条原因 → 账本逐条记录、互不顶替；数字按玩家去重。</summary>
    [Fact]
    public void MultipleCauses_LeaveOneEntryEach()
    {
        var state = GameStateMachine.Fold(
        [
            Resolved(effective: false, MalfunctionKind.Poisoned, MalfunctionKind.Drunk),
        ]);

        Assert.Equal(2, state.Malfunctions.Count);
        Assert.Equal(
            new[] { MalfunctionKind.Poisoned, MalfunctionKind.Drunk },
            state.Malfunctions.Entries.Select(entry => entry.Kind));
        Assert.Equal(new[] { new SeatId(2) }, state.Malfunctions.CountedSeats);
    }

    private static AbilityResolvedEvent Resolved(bool effective, params MalfunctionKind[] malfunctions) => new()
    {
        SlotId = new StepSlotId("dreamer"),
        Actor = new SeatId(2),
        Ability = new AbilityId("dreamer"),
        Effective = effective,
        Malfunctions = malfunctions,
    };
}
