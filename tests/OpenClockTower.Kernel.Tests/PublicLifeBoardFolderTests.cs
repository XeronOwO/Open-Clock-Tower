using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 公开生死面折叠：夜晚变化累积到黎明、按"相对黄昏的净变化"公告；白天变化即时公告；
/// 未公告的状态对玩家不可见（含本人）。本面不改变真实生死账。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0022（引百科《规则概要》黎明段、《术语汇总》生命标记、
/// 《夜晚行动顺序一览》黎明、《女巫》"立即宣布"）与 D-0015（账本只记事实，公开面是另一份投影）。
/// </remarks>
public sealed class PublicLifeBoardFolderTests
{
    /// <summary>首个黎明之前：观测只补折叠态，没有"公告"这个时点，也不构成对外可观察的变化。</summary>
    [Fact]
    public void BeforeFirstDawn_ObservationEntersTheBoardWithoutAnnouncement()
    {
        var board = Fold(Life(1, LifeState.Alive), Life(2, LifeState.Dead));

        AssertLife(board, (1, LifeState.Alive), (2, LifeState.Dead));
        Assert.Empty(board.Announcements);
        Assert.Equal(0, board.PublicRevision);
    }

    /// <summary>夜晚死亡在黎明前不可见（牌面不动），黎明随开白天一次性公告。</summary>
    [Fact]
    public void NightDeath_IsInvisibleUntilDawn_ThenAnnouncedWithBoardUpdate()
    {
        var atDusk = Fold(Life(1, LifeState.Alive), Life(2, LifeState.Alive), Night(GamePhase.FirstNight));
        AssertLife(atDusk, (1, LifeState.Alive), (2, LifeState.Alive));

        var duringNight = PublicLifeBoardFolder.Apply(atDusk, Life(2, LifeState.Dead));
        AssertLife(duringNight, (1, LifeState.Alive), (2, LifeState.Alive));
        Assert.Empty(duringNight.Announcements);
        Assert.Equal(atDusk.PublicRevision, duringNight.PublicRevision);

        var dawn = PublicLifeBoardFolder.Apply(duringNight, DayStarted(1));
        AssertLife(dawn, (1, LifeState.Alive), (2, LifeState.Dead));
        AssertAnnouncements(dawn, (2, LifeState.Dead));
        Assert.Equal(atDusk.PublicRevision + 1, dawn.PublicRevision);
    }

    /// <summary>夜里死而复生：相对黄昏没有净变化 → 不公告、牌面不留痕。</summary>
    [Fact]
    public void DiedAndRevivedDuringTheNight_LeavesNoTrace()
    {
        var board = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            Life(1, LifeState.Dead),
            Life(1, LifeState.Alive),
            DayStarted(1));

        AssertLife(board, (1, LifeState.Alive));
        Assert.Empty(board.Announcements);
        Assert.Equal(0, board.PublicRevision);
    }

    /// <summary>黄昏已死亡、夜里复活：黎明公告"复活"（复活与死亡同列公告）。</summary>
    [Fact]
    public void NightRevival_IsAnnouncedAsRevived()
    {
        var board = Fold(
            Life(1, LifeState.Dead),
            Night(GamePhase.OtherNight),
            Life(1, LifeState.Alive),
            DayStarted(2));

        AssertLife(board, (1, LifeState.Alive));
        AssertAnnouncements(board, (1, LifeState.Alive));
    }

    /// <summary>同一夜多名玩家变化：只按"最新值 vs 黄昏"公告，且顺序固定按席位升序。</summary>
    [Fact]
    public void MultipleNightChanges_OnlyTheNetChangeIsAnnounced_OrderedBySeat()
    {
        var board = Fold(
            Life(3, LifeState.Alive),
            Life(1, LifeState.Alive),
            Life(2, LifeState.Alive),
            Night(GamePhase.FirstNight),
            Life(3, LifeState.Dead),
            Life(1, LifeState.Dead),
            Life(3, LifeState.Alive),
            DayStarted(1));

        AssertLife(board, (1, LifeState.Dead), (2, LifeState.Alive), (3, LifeState.Alive));
        AssertAnnouncements(board, (1, LifeState.Dead));
    }

    /// <summary>白天死亡即时公告（女巫"立即宣布"的等价物），并更新公开牌面。</summary>
    [Fact]
    public void DayDeath_IsAnnouncedImmediately()
    {
        var board = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            DayStarted(1),
            Life(1, LifeState.Dead));

        AssertLife(board, (1, LifeState.Dead));
        AssertAnnouncements(board, (1, LifeState.Dead));
    }

    /// <summary>同值重报是幂等的：不上屏、不公告、不改版本号（含夜间累积）。</summary>
    [Fact]
    public void SameValueReport_IsIdempotent()
    {
        var day = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            DayStarted(1),
            Life(1, LifeState.Dead));

        var repeated = PublicLifeBoardFolder.Apply(day, Life(1, LifeState.Dead));
        Assert.Same(day, repeated);

        var atDusk = Fold(Life(1, LifeState.Alive), Life(2, LifeState.Alive), Night(GamePhase.FirstNight));
        var first = PublicLifeBoardFolder.Apply(atDusk, Life(2, LifeState.Dead));
        var second = PublicLifeBoardFolder.Apply(first, Life(2, LifeState.Dead));
        Assert.Equal(first.PublicRevision, second.PublicRevision);
        Assert.Single(second.Pending);
    }

    /// <summary>无变化的黎明：公告清空（新的一天没有公告），牌面与版本号如实反映变化。</summary>
    [Fact]
    public void DawnWithoutChanges_ClearsThePreviousDaysAnnouncements()
    {
        var dayOne = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            DayStarted(1),
            Life(1, LifeState.Dead));
        Assert.Single(dayOne.Announcements);

        var dayTwo = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            DayStarted(1),
            Life(1, LifeState.Dead),
            DayClosed(1),
            Night(GamePhase.OtherNight),
            DayStarted(2));

        AssertLife(dayTwo, (1, LifeState.Dead));
        Assert.Empty(dayTwo.Announcements);
        Assert.True(dayTwo.PublicRevision > dayOne.PublicRevision);
    }

    /// <summary>黄昏没有基准的席位（从未观测到生死）：夜里观测只补牌面，不凭"第一次看见"断言死亡。</summary>
    [Fact]
    public void UnobservedSeatAtDusk_OnlyFillsTheBoard_WithoutAnnouncement()
    {
        var board = Fold(
            Life(1, LifeState.Alive),
            Night(GamePhase.FirstNight),
            Life(2, LifeState.Dead),
            DayStarted(1));

        AssertLife(board, (1, LifeState.Alive), (2, LifeState.Dead));
        Assert.Empty(board.Announcements);
    }

    /// <summary>黄昏边界：白天已关、夜晚未开的间隙（R-0022 登记的黄昏边界；与 R-0020 第 6 条同族）按夜晚形态累积到下一个黎明。</summary>
    [Fact]
    public void LifeChangeBetweenDuskAndNightStart_IsDeferredToTheNextDawn()
    {
        var dusk = Fold(Life(1, LifeState.Alive), Night(GamePhase.FirstNight), DayStarted(1), DayClosed(1));
        var gap = PublicLifeBoardFolder.Apply(dusk, Life(1, LifeState.Dead));
        Assert.Empty(gap.Announcements);

        var dawn = PublicLifeBoardFolder.Apply(
            PublicLifeBoardFolder.Apply(gap, Night(GamePhase.OtherNight)),
            DayStarted(2));
        AssertAnnouncements(dawn, (1, LifeState.Dead));
    }

    /// <summary>无关事件原样返回同一份面（不产生伪变化、不误推）。</summary>
    [Fact]
    public void UnrelatedEvents_ReturnTheSameBoard()
    {
        var board = Fold(Life(1, LifeState.Alive));
        var unchanged = PublicLifeBoardFolder.Apply(
            board,
            new NominationMadeEvent
            {
                DayNumber = 1,
                NominationIndex = 1,
                Nominator = new SeatId(1),
                Nominee = new SeatId(2),
            });

        Assert.Same(board, unchanged);
    }

    /// <summary>折叠是确定性的：重复折叠与逐条折叠等价（重放 / 重建的基础）。</summary>
    [Fact]
    public void FoldingIsDeterministic_AndMatchesIncrementalApplication()
    {
        GameEvent[] events =
        [
            Life(1, LifeState.Alive),
            Life(2, LifeState.Alive),
            Night(GamePhase.FirstNight),
            Life(2, LifeState.Dead),
            DayStarted(1),
            Life(1, LifeState.Dead),
        ];

        var once = PublicLifeBoardFolder.ApplyAll(PublicLifeBoard.Empty, events);
        var twice = PublicLifeBoardFolder.ApplyAll(PublicLifeBoard.Empty, events);
        var incremental = PublicLifeBoard.Empty;
        foreach (var gameEvent in events)
        {
            incremental = PublicLifeBoardFolder.Apply(incremental, gameEvent);
        }

        Assert.Equal(once.Lives, twice.Lives);
        Assert.Equal(once.Announcements, twice.Announcements);
        Assert.Equal(once.Lives, incremental.Lives);
        Assert.Equal(once.Announcements, incremental.Announcements);
        Assert.Equal(once.PublicRevision, incremental.PublicRevision);
    }

    /// <summary>
    /// 旅行者离场：生命标记从公开生死面撤下（百科《旅行者》· 离开流程），并推进公开版本号；
    /// 本日已经发生的公告是历史事实，不随离场抹掉。
    /// </summary>
    [Fact]
    public void TravellerDeparture_RemovesTheSeatFromThePublicBoard()
    {
        var board = Fold(
            Life(1, LifeState.Alive),
            Life(2, LifeState.Alive),
            DayStarted(1),
            Life(2, LifeState.Dead));

        var departed = PublicLifeBoardFolder.Apply(
            board,
            new TravellerDepartedEvent { Seat = new SeatId(2), Note = "测试：离场" });

        AssertLife(departed, (1, LifeState.Alive));
        Assert.Equal(board.PublicRevision + 1, departed.PublicRevision);

        // 本日公告是已发生的事实，不随离场抹掉（公告只说"当时发生了什么"）。
        Assert.Single(departed.Announcements);

        // 对不在公开面上的席位离场：没有变化，原样返回（不动版本号）。
        var untouched = PublicLifeBoardFolder.Apply(departed, new TravellerDepartedEvent { Seat = new SeatId(9) });
        Assert.Same(departed, untouched);
    }

    private static SeatStateChangedEvent Life(int seat, LifeState life) => new()
    {
        Seat = new SeatId(seat),
        Life = life,
        Reason = "测试：生死观测",
    };

    private static PhaseStartedEvent Night(GamePhase phase) => new()
    {
        Plan = new StepPlan { Label = $"test:{phase}", Phase = phase, Slots = [] },
        Control = ControlMode.Automatic,
    };

    private static DayStartedEvent DayStarted(int dayNumber) => new() { DayNumber = dayNumber };

    private static DayClosedEvent DayClosed(int dayNumber) => new() { DayNumber = dayNumber };

    private static PublicLifeBoard Fold(params GameEvent[] events) =>
        PublicLifeBoardFolder.ApplyAll(PublicLifeBoard.Empty, events);

    private static void AssertLife(PublicLifeBoard board, params (int Seat, LifeState State)[] expected) =>
        Assert.Equal(
            expected.Select(item => (item.Seat, item.State)),
            board.Lives.Select(entry => (entry.Seat.Value, entry.State)));

    private static void AssertAnnouncements(PublicLifeBoard board, params (int Seat, LifeState State)[] expected) =>
        Assert.Equal(
            expected.Select(item => (item.Seat, item.State)),
            board.Announcements.Select(entry => (entry.Seat.Value, entry.State)));
}
