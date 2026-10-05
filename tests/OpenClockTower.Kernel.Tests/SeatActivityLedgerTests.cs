using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 近期活动账：谁死了 / 谁换了角色或阵营 / 谁被处决 / 谁的能力没正常生效，
/// 以及「最近一个已结束的夜晚」与「当前白天」两个窗口的推进（R-0057-C）。
/// </summary>
/// <remarks>
/// 记账口径保守：**变化前后都已知**才算一次变化——开局分配与首次观测是"知道"，不是"变化"。
/// 理由见票据「要解决的问题」第 2 条：候选事实库会把这些当成"真"讲给玩家听，假阳性等于平台在骗说书人。
/// </remarks>
public sealed class SeatActivityLedgerTests
{
    private static readonly SeatId SeatOne = new(1);
    private static readonly SeatId SeatTwo = new(2);
    private static readonly SeatId SeatThree = new(3);

    /// <summary>开局分配与首次观测不是「变化」：账上没有死亡 / 换角 / 换阵营的记录。</summary>
    [Fact]
    public void SetupObservations_AreNotChanges()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = SeatOne,
                Life = LifeState.Alive,
                Character = new CharacterId("savant"),
                Alignment = Alignment.Good,
                Reason = "开局：说书人分配",
            },
            new SeatStateChangedEvent
            {
                Seat = SeatTwo,
                Life = LifeState.Dead,
                Character = new CharacterId("witch"),
                Alignment = Alignment.Evil,
                Reason = "说书人上报：这一席已经死了",
            },
        ]);

        Assert.Empty(state.Activity.Entries);
    }

    /// <summary>存活 → 死亡记一条；已死再报一次不重复记（不是一次新的死亡）。</summary>
    [Fact]
    public void Death_IsRecordedOnlyOnTheAliveToDeadTransition()
    {
        var state = GameStateMachine.Fold(
        [
            Alive(SeatOne),
            new SeatStateChangedEvent { Seat = SeatOne, Life = LifeState.Dead, Reason = "被恶魔击杀" },
            new SeatStateChangedEvent { Seat = SeatOne, Life = LifeState.Dead, Reason = "复盘时再次确认" },
        ]);

        var activity = Assert.Single(state.Activity.Entries);
        Assert.Equal(SeatActivityKind.Death, activity.Kind);
        Assert.Equal(SeatOne, activity.Seat);
        Assert.Equal("被恶魔击杀", activity.Reason);
    }

    /// <summary>角色变化要求变化前后都已知：换角记一条，同角色的重复观测不记。</summary>
    [Fact]
    public void CharacterChange_RequiresBothEndsKnown()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent { Seat = SeatTwo, Character = new CharacterId("dreamer"), Reason = "开局" },
            new SeatStateChangedEvent
            {
                Seat = SeatTwo,
                Character = new CharacterId("sage"),
                Reason = "理发师交换",
            },
            new SeatStateChangedEvent { Seat = SeatTwo, Character = new CharacterId("sage"), Reason = "重复上报" },
        ]);

        var activity = Assert.Single(state.Activity.Entries);
        Assert.Equal(SeatActivityKind.CharacterChange, activity.Kind);
        Assert.Equal("理发师交换", activity.Reason);
    }

    /// <summary>阵营变化同样要求前后都已知（方古侵染这类只改阵营、不改角色）。</summary>
    [Fact]
    public void AlignmentChange_RequiresBothEndsKnown()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent { Seat = SeatThree, Alignment = Alignment.Good, Reason = "开局" },
            new SeatStateChangedEvent { Seat = SeatThree, Alignment = Alignment.Evil, Reason = "方古侵染" },
        ]);

        var activity = Assert.Single(state.Activity.Entries);
        Assert.Equal(SeatActivityKind.AlignmentChange, activity.Kind);
        Assert.Equal(SeatThree, activity.Seat);
    }

    /// <summary>夜晚窗口：开夜之后、黎明之前发生的活动只在「昨晚」；黎明之后发生的只在「今天」。</summary>
    [Fact]
    public void Windows_SplitLastNightFromToday()
    {
        var state = GameStateMachine.Fold(
        [
            Alive(SeatOne),
            Alive(SeatTwo),
            Night(),
            new SeatStateChangedEvent { Seat = SeatOne, Life = LifeState.Dead, Reason = "被恶魔击杀" },
            Day(),
            new ExecutedEvent { DayNumber = 1, Seat = SeatTwo, Kind = ExecutionKind.Day },
            new SeatStateChangedEvent { Seat = SeatTwo, Life = LifeState.Dead, Reason = "被处决" },
        ]);

        Assert.True(state.Activity.AnyLastNight(SeatActivityKind.Death));
        Assert.True(state.Activity.AnyToday(SeatActivityKind.Death));
        Assert.True(state.Activity.AnyToday(SeatActivityKind.Execution));
        Assert.False(state.Activity.AnyLastNight(SeatActivityKind.Execution));

        Assert.Equal(SeatOne, Assert.Single(state.Activity.LastNight).Seat);
        Assert.Equal(2, state.Activity.Today.Count);

        // 记录只增不删：窗口靠下标推进，不删记录（审计与复算优先）。
        Assert.Equal(3, state.Activity.Entries.Count);
    }

    /// <summary>夜晚的处罚处决不落在"今天"（R-0020：夜晚处决不占任何白天的上限）。</summary>
    [Fact]
    public void NightExecution_StaysInTheNightWindow()
    {
        var state = GameStateMachine.Fold(
        [
            Alive(SeatOne),
            Night(),
            new ExecutedEvent { DayNumber = null, Seat = SeatOne, Kind = ExecutionKind.CerenovusMadness, Note = "疯狂处罚" },
            Day(),
        ]);

        Assert.False(state.Activity.AnyToday(SeatActivityKind.Execution));
        Assert.True(state.Activity.AnyLastNight(SeatActivityKind.Execution));
    }

    /// <summary>还没有黎明时没有"已结束的夜晚"：夜晚窗口为空，白天窗口从账的开头算起。</summary>
    [Fact]
    public void BeforeTheFirstDawn_ThereIsNoCompletedNight()
    {
        var state = GameStateMachine.Fold(
        [
            Alive(SeatOne),
            Night(),
            new SeatStateChangedEvent { Seat = SeatOne, Life = LifeState.Dead, Reason = "被恶魔击杀" },
        ]);

        Assert.Null(state.Activity.LastNightEnd);
        Assert.Empty(state.Activity.LastNight);
        Assert.True(state.Activity.AnyToday(SeatActivityKind.Death));
    }

    /// <summary>能力未正常生效：只有**计入数学家的**分类进活动账（R-0004 的同一份口径）。</summary>
    [Fact]
    public void Malfunction_RecordsOnlyTheCountedKinds()
    {
        var state = GameStateMachine.Fold(
        [
            Resolved(MalfunctionKind.Poisoned),
            Resolved(MalfunctionKind.Drunk),
            Resolved(MalfunctionKind.Vortox),
            Resolved(MalfunctionKind.Jinx),
            Resolved(MalfunctionKind.Barista),
        ]);

        Assert.Equal(3, state.Activity.Entries.Count);
        Assert.All(state.Activity.Entries, entry => Assert.Equal(SeatActivityKind.Malfunction, entry.Kind));
        Assert.Equal("来源中毒：能力未生效", state.Activity.Entries[0].Reason);
    }

    /// <summary>正常生效的结算不进活动账（没有失效可言）。</summary>
    [Fact]
    public void EffectiveAbility_IsNotRecorded()
    {
        var state = GameStateMachine.Fold([Resolved(null)]);

        Assert.Empty(state.Activity.Entries);
    }

    private static SeatStateChangedEvent Alive(SeatId seat) => new()
    {
        Seat = seat,
        Life = LifeState.Alive,
        Reason = "开局：说书人分配",
    };

    private static PhaseStartedEvent Night() => new()
    {
        Plan = new StepPlan { Label = "sv:night-2", Phase = GamePhase.OtherNight, Slots = [] },
        Control = ControlMode.Automatic,
    };

    private static DayStartedEvent Day() => new() { DayNumber = 1 };

    private static AbilityResolvedEvent Resolved(MalfunctionKind? kind) => new()
    {
        SlotId = new StepSlotId("sv:test"),
        Actor = SeatTwo,
        Ability = new AbilityId("clockmaker"),
        Effective = kind is null,
        Malfunctions = kind is { } value ? [value] : [],
        Note = kind is null ? null : "来源中毒：能力未生效",
    };
}
