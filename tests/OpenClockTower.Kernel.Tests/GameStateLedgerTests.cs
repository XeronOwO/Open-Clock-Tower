using OpenClockTower.Kernel;
using static OpenClockTower.Kernel.Tests.StepFixture;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 状态账：只记已观测维度、每个维度各自带归因、六个维度互不耦合。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三（状态属于玩家、六维度相互独立），D-0002（平台只记录与推演）。
/// </remarks>
public sealed class GameStateLedgerTests
{
    private static readonly SeatId SeatThree = new(3);
    private static readonly SeatId SeatFive = new(5);
    private static readonly SeatId SeatSeven = new(7);

    /// <summary>只记观测到的维度，且每个维度保住自己的归因（后来的变化不冲掉别人的原因）。</summary>
    [Fact]
    public void ObservedDimensions_CarryTheirOwnAttribution()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = SeatThree,
                Life = LifeState.Alive,
                Reason = "开局：说书人分配",
            },
            new SeatStateChangedEvent
            {
                Seat = SeatThree,
                Poison = PoisonState.Poisoned,
                Reason = "投毒者能力",
                CausedBy = SeatFive,
            },
        ]);

        var entry = Assert.IsType<SeatStateEntry>(state.Seat(SeatThree));
        Assert.Equal(LifeState.Alive, entry.LifeValue);
        Assert.Equal("开局：说书人分配", entry.Life!.Reason);
        Assert.Null(entry.Life.CausedBy);

        Assert.Equal(PoisonState.Poisoned, entry.PoisonValue);
        Assert.Equal("投毒者能力", entry.Poison!.Reason);
        Assert.Equal(SeatFive, entry.Poison.CausedBy);

        Assert.Null(entry.Character);
        Assert.Null(entry.Alignment);
        Assert.Null(entry.Drunk);
    }

    /// <summary>一个维度变了不得带改另一个维度（六维度正交）；中毒的归因也不能被角色变化冲掉。</summary>
    [Fact]
    public void ChangingOneDimension_LeavesOtherDimensionsAndAttributionsAlone()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = SeatThree,
                Life = LifeState.Alive,
                Poison = PoisonState.Poisoned,
                Reason = "投毒者能力",
                CausedBy = SeatFive,
            },
            new SeatStateChangedEvent
            {
                Seat = SeatThree,
                Character = new CharacterId("barber"),
                Reason = "理发师：交换角色",
                CausedBy = SeatSeven,
            },
        ]);

        var entry = Assert.IsType<SeatStateEntry>(state.Seat(SeatThree));
        Assert.Equal(new CharacterId("barber"), entry.CharacterValue);
        Assert.Equal("理发师：交换角色", entry.Character!.Reason);

        Assert.Equal(PoisonState.Poisoned, entry.PoisonValue);
        Assert.Equal("投毒者能力", entry.Poison!.Reason);
        Assert.Equal(SeatFive, entry.Poison.CausedBy);
        Assert.Equal(LifeState.Alive, entry.LifeValue);
    }

    /// <summary>五个维度没观测齐之前拿不到完整状态——缺哪维就说缺哪维，不许拿默认值凑。</summary>
    [Fact]
    public void KnownState_StaysUnknownUntilAllFiveDimensionsAreObserved()
    {
        var partial = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent { Seat = SeatThree, Life = LifeState.Alive, Reason = "第一次观测" },
        ]);

        Assert.Null(partial.KnownStateOf(SeatThree));
        Assert.False(Assert.IsType<SeatStateEntry>(partial.Seat(SeatThree)).IsComplete);

        var complete = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent
            {
                Seat = SeatThree,
                Life = LifeState.Alive,
                Character = new CharacterId("clockmaker"),
                Alignment = Alignment.Good,
                Drunk = DrunkState.Drunk,
                Poison = PoisonState.Poisoned,
                Reason = "开局：说书人分配",
            },
        ]);

        var seatState = complete.KnownStateOf(SeatThree);
        Assert.NotNull(seatState);
        Assert.Equal(new CharacterId("clockmaker"), seatState.Character);
        Assert.Equal(Alignment.Good, seatState.Alignment);
        Assert.Equal(LifeState.Alive, seatState.Life);
        Assert.Equal(DrunkState.Drunk, seatState.Drunk);
        Assert.Equal(PoisonState.Poisoned, seatState.Poison);
    }

    /// <summary>座位顺序由账自己决定（按席位号升序），与事件到达顺序无关（D-0008）。</summary>
    [Fact]
    public void Seats_AreKeptInSeatOrder_RegardlessOfArrivalOrder()
    {
        var state = GameStateMachine.Fold(
        [
            new SeatStateChangedEvent { Seat = SeatSeven, Life = LifeState.Alive, Reason = "先报 7 号" },
            new SeatStateChangedEvent { Seat = SeatThree, Life = LifeState.Alive, Reason = "再报 3 号" },
            new SeatStateChangedEvent { Seat = SeatFive, Life = LifeState.Dead, Reason = "最后报 5 号" },
        ]);

        Assert.Equal(
            [SeatThree, SeatFive, SeatSeven],
            state.Seats.Select(entry => entry.Seat));
    }

    /// <summary>整条流折叠与逐条折叠必须得到同一个账（重放可信的前提）。</summary>
    [Fact]
    public void Fold_EqualsApplyingEventsOneByOne()
    {
        GameEvent[] events =
        [
            new SeatStateChangedEvent { Seat = SeatThree, Life = LifeState.Alive, Reason = "第一次观测" },
            new SeatStateChangedEvent { Seat = SeatFive, Poison = PoisonState.Poisoned, Reason = "投毒者能力", CausedBy = SeatSeven },
            new SeatStateChangedEvent { Seat = SeatThree, Drunk = DrunkState.Drunk, Reason = "涡流能力", CausedBy = null },
        ];

        var folded = GameStateMachine.Fold(events);

        var incremental = GameState.Empty;
        foreach (var gameEvent in events)
        {
            incremental = GameStateMachine.Apply(incremental, gameEvent);
        }

        Assert.Equal(
            folded.Seats.Select(entry => (entry.Seat, entry.LifeValue, entry.DrunkValue, entry.PoisonValue, entry.Poison?.Reason)),
            incremental.Seats.Select(entry => (entry.Seat, entry.LifeValue, entry.DrunkValue, entry.PoisonValue, entry.Poison?.Reason)));
    }

    /// <summary>空流是合法输入：还没观测到任何东西 = 空账。</summary>
    [Fact]
    public void Fold_OfEmptyStream_IsAnEmptyLedger()
    {
        var state = GameStateMachine.Fold([]);

        Assert.Empty(state.Seats);
        Assert.Empty(state.PersistentEffects);
        Assert.Empty(state.InstantaneousEffects);
    }

    /// <summary>一条不带任何观测维度的状态变化是损坏数据，必须当场炸掉，不许静默建出一行空账。</summary>
    [Fact]
    public void StateChangeWithoutObservedDimension_IsRejectedLoudly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(
            GameState.Empty,
            new SeatStateChangedEvent { Seat = SeatThree, Reason = "没有任何维度" }));

        Assert.Contains("观测维度", exception.Message);
    }

    /// <summary>不认识的事件类型必须当场炸掉：将来新增事件却忘了接进账里，不能悄悄少记一笔。</summary>
    [Fact]
    public void UnknownEventType_IsRejectedLoudly()
    {
        Assert.Throws<InvalidOperationException>(() => GameStateMachine.Apply(GameState.Empty, new UnknownEvent()));
    }

    /// <summary>步骤机侧同样拦住"没有任何维度"的状态变化输入，走拒绝路径而不是抛异常。</summary>
    [Fact]
    public void StepMachine_RejectsStateChangeWithoutObservedDimension()
    {
        var started = StepMachine.StartPhase(Plan("sv:night-1", Empty("empty-1"))).State;

        var outcome = StepMachine.Handle(
            started,
            new SeatStateChangedInput { Seat = SeatThree, Reason = "没有任何维度" });

        Assert.Equal(StepMachineOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(StepMachineRejectionReason.UnexpectedInput, outcome.RejectionReason);
        Assert.Empty(outcome.Events);
    }

    /// <summary>测试专用的"未知事件"，用来证明折叠对陌生事件不会装看不见。</summary>
    private sealed record UnknownEvent : GameEvent;
}
