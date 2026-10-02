using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 会话跟踪器的公开生死面：增量提交与全量重放得到同一份公开面；
/// <c>Update</c> 的返回值只在**公开面实际变化**时为 true——夜晚挂起不算，
/// 否则一条夜里的死亡会变成"房间里有人活动"的推送指示（D-0013 §5）。
/// </summary>
/// <remarks>依据 D-0010（重放 = 折叠事件；恢复不重算）与 `rulings.md` R-0022（公开面时点）。</remarks>
public sealed class SessionTrackersPublicLifeTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>全量 Recover 与逐批 Update 折叠出同一份公开面（重启 / 重建的一致性基础）。</summary>
    [Fact]
    public void Recover_FoldsTheSamePublicSurfaceAsIncrementalUpdate()
    {
        GameEvent[] events =
        [
            Life(1, LifeState.Alive),
            Life(2, LifeState.Alive),
            Night(GamePhase.FirstNight),
            Life(2, LifeState.Dead),
            new DayStartedEvent { DayNumber = 1 },
            Life(1, LifeState.Dead),
        ];

        var stored = events
            .Select((gameEvent, index) => new StoredEvent
            {
                Sequence = index + 1,
                Event = gameEvent,
                RecordedAt = Epoch,
            })
            .ToArray();

        var recovered = new SessionTrackers();
        recovered.Recover(stored, machine: null);

        var incremental = new SessionTrackers();
        foreach (var item in stored)
        {
            incremental.Update([Draft(item.Sequence, item.Event)], Epoch);
        }

        Assert.Equal(recovered.PublicLife.Lives, incremental.PublicLife.Lives);
        Assert.Equal(recovered.PublicLife.Announcements, incremental.PublicLife.Announcements);
        Assert.Equal(recovered.PublicLife.PublicRevision, incremental.PublicLife.PublicRevision);
    }

    /// <summary>只有**对外可观察**的公开面变化才返回 true：黎明公开 / 白天变化为 true，开局补观测、夜晚挂起、同值重报为 false。</summary>
    [Fact]
    public void Update_ReportsPublicSurfaceChangesOnly()
    {
        var trackers = new SessionTrackers();

        // 首个黎明之前：玩家端还没有白天投影（Day = null），补观测不构成对外可观察的变化。
        Assert.False(trackers.Update([Draft(1, Life(1, LifeState.Alive))], Epoch));

        // 夜晚：变化只累积，公开面不动——返回 false，不产生任何在线指示。
        Assert.False(trackers.Update([Draft(2, Night(GamePhase.FirstNight))], Epoch));
        Assert.False(trackers.Update([Draft(3, Life(1, LifeState.Dead))], Epoch));
        Assert.Empty(trackers.PublicLife.Announcements);

        // 黎明：挂起的死亡公开，返回 true。
        Assert.True(trackers.Update([Draft(4, new DayStartedEvent { DayNumber = 1 })], Epoch));
        Assert.Contains(
            trackers.PublicLife.Announcements,
            entry => entry.Seat.Value == 1 && entry.State == LifeState.Dead);

        // 同值重报：不产生伪变化。
        Assert.False(trackers.Update([Draft(5, Life(1, LifeState.Dead))], Epoch));

        // Clear 把公开面复位（事件流损坏、房间停在空状态时）。
        trackers.Clear();
        Assert.Empty(trackers.PublicLife.Lives);
        Assert.Empty(trackers.PublicLife.Announcements);
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

    private static StoredEventDraft Draft(long sequence, GameEvent gameEvent) => new()
    {
        Sequence = sequence,
        Event = gameEvent,
        RecordedAt = Epoch,
    };
}
