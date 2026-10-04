using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 复盘投影的纯逻辑契约：顺序保真（= 事件序号）、分页不重不漏、排除项不进时间轴、
/// 恶魔击杀箭头 / 死亡帷幕 / 换角标记与席位增量（D-0020 / 票据矩阵行 3–6）。
/// </summary>
public sealed class ReplayProjectionTests
{
    private static StoredEvent Assignment(long sequence, int seat, string character) => new()
    {
        Sequence = sequence,
        RecordedAt = DateTimeOffset.UnixEpoch,
        Event = new SeatStateChangedEvent
        {
            Seat = new SeatId(seat),
            Character = new CharacterId(character),
            Life = LifeState.Alive,
            Reason = "测试：开局分配",
        },
    };

    private static StoredEvent Death(long sequence, int seat, int causedBy, string reason) => new()
    {
        Sequence = sequence,
        RecordedAt = DateTimeOffset.UnixEpoch,
        Event = new SeatStateChangedEvent
        {
            Seat = new SeatId(seat),
            Life = LifeState.Dead,
            Reason = reason,
            CausedBy = new SeatId(causedBy),
        },
    };

    /// <summary>空事件流 = 空复盘（还没观测到任何东西是合法状态）。</summary>
    [Fact]
    public void EmptyStream_YieldsEmptyView()
    {
        var view = ReplayProjection.Build([], afterSequence: 0, pageSize: 100);

        Assert.Equal(0, view.Sequence);
        Assert.False(view.Ended);
        Assert.False(view.HasMore);
        Assert.Empty(view.Steps);
    }

    /// <summary>步骤顺序 = 事件序号顺序；分页按序号推进、不重不漏。</summary>
    [Fact]
    public void Steps_FollowEventSequence_AndPagingAdvances()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "vortox"),
            Assignment(2, 2, "clockmaker"),
            Assignment(3, 3, "dreamer"),
            Death(4, 2, causedBy: 1, reason: "测试：恶魔击杀"),
        };

        var all = ReplayProjection.Build(stream, afterSequence: 0, pageSize: 100);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, all.Steps.Select(step => step.Sequence).ToArray());
        Assert.False(all.HasMore);
        Assert.False(all.Ended);

        var first = ReplayProjection.Build(stream, afterSequence: 0, pageSize: 2);
        Assert.Equal(new long[] { 1, 2 }, first.Steps.Select(step => step.Sequence).ToArray());
        Assert.True(first.HasMore);

        var second = ReplayProjection.Build(stream, afterSequence: 2, pageSize: 2);
        Assert.Equal(new long[] { 3, 4 }, second.Steps.Select(step => step.Sequence).ToArray());
        Assert.False(second.HasMore);

        var beyond = ReplayProjection.Build(stream, afterSequence: 4, pageSize: 2);
        Assert.Empty(beyond.Steps);
        Assert.False(beyond.HasMore);
    }

    /// <summary>恶魔击杀：红色箭头（from → to）+ 死亡帷幕 + 席位生死增量。</summary>
    [Fact]
    public void DemonKill_ProducesArrowAndShroud_WithSeatDelta()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "vortox"),
            Assignment(2, 2, "clockmaker"),
            Death(3, 2, causedBy: 1, reason: "测试：恶魔击杀"),
        };

        var step = ReplayProjection.Build(stream, 0, 100).Steps.Single(item => item.Sequence == 3);

        var delta = Assert.Single(step.Seats);
        Assert.Equal(new SeatId(2), delta.Seat);
        Assert.Equal(LifeState.Dead, delta.Life);
        Assert.Contains("恶魔击杀", delta.Reason);

        Assert.Contains(
            step.Markers,
            marker => marker.Kind == "kill-arrow"
                && marker.From == new SeatId(1)
                && marker.To == new SeatId(2));
        Assert.Contains(
            step.Markers,
            marker => marker.Kind == "shroud" && marker.Seat == new SeatId(2));
    }

    /// <summary>非恶魔造成的死亡只画死亡帷幕、不画击杀箭头（判据与 R-0038 同源）。</summary>
    [Fact]
    public void NonDemonDeath_HasNoKillArrow()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "clockmaker"),
            Assignment(2, 2, "dreamer"),
            Death(3, 2, causedBy: 1, reason: "测试：处决"),
        };

        var step = ReplayProjection.Build(stream, 0, 100).Steps.Single(item => item.Sequence == 3);

        Assert.Contains(step.Markers, marker => marker.Kind == "shroud");
        Assert.DoesNotContain(step.Markers, marker => marker.Kind == "kill-arrow");
    }

    /// <summary>换角：标记带「从 X 到 Y」的文本，且增量保留前角色（矩阵行 3 的「换角」标记）。</summary>
    [Fact]
    public void CharacterChange_ProducesChangeMarker()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "clockmaker"),
            new()
            {
                Sequence = 2,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatStateChangedEvent
                {
                    Seat = new SeatId(1),
                    Character = new CharacterId("dreamer"),
                    PreviousCharacter = new CharacterId("clockmaker"),
                    Reason = "测试：换角",
                },
            },
        };

        var step = ReplayProjection.Build(stream, 0, 100).Steps.Single(item => item.Sequence == 2);

        var marker = Assert.Single(step.Markers, item => item.Kind == "character-change");
        Assert.Contains("钟表匠", marker.Text);
        Assert.Contains("筑梦师", marker.Text);
        Assert.Equal(new CharacterId("clockmaker"), Assert.Single(step.Seats).PreviousCharacter);
    }

    /// <summary>中毒 / 醉酒各自有标记（矩阵行 3）；恢复方向（健康 / 清醒）不误标。</summary>
    [Fact]
    public void PoisonAndDrunk_ProduceMarkersOnlyWhenApplied()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "clockmaker"),
            new()
            {
                Sequence = 2,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatStateChangedEvent
                {
                    Seat = new SeatId(1),
                    Poison = PoisonState.Poisoned,
                    Drunk = DrunkState.Drunk,
                    Reason = "测试：中毒且醉酒",
                },
            },
            new()
            {
                Sequence = 3,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatStateChangedEvent
                {
                    Seat = new SeatId(1),
                    Poison = PoisonState.Healthy,
                    Drunk = DrunkState.Sober,
                    Reason = "测试：恢复",
                },
            },
        };

        var steps = ReplayProjection.Build(stream, 0, 100).Steps;
        var applied = steps.Single(item => item.Sequence == 2);
        Assert.Contains(applied.Markers, marker => marker.Kind == "poisoned");
        Assert.Contains(applied.Markers, marker => marker.Kind == "drunk");

        var recovered = steps.Single(item => item.Sequence == 3);
        Assert.DoesNotContain(recovered.Markers, marker => marker.Kind == "poisoned");
        Assert.DoesNotContain(recovered.Markers, marker => marker.Kind == "drunk");
    }

    /// <summary>说书人注记被排除：不进时间轴，也不改变序号顺序（D-0019）。</summary>
    [Fact]
    public void SeatAnnotations_AreExcludedFromTimeline()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 1, "clockmaker"),
            new()
            {
                Sequence = 2,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatAnnotationAddedEvent
                {
                    Annotation = new SeatAnnotation(new SeatAnnotationId(1), new SeatId(1), "测试注记"),
                },
            },
            Death(3, 1, causedBy: 1, reason: "测试：死亡"),
        };

        var steps = ReplayProjection.Build(stream, 0, 100).Steps;
        Assert.Equal(new long[] { 1, 3 }, steps.Select(step => step.Sequence).ToArray());
    }

    /// <summary>
    /// 大事件流（≥ 2000 事件）按默认 200 / 页分页：中间页必为满页、序号严格递增、
    /// 逐页拉完与整条事件流一比一（不重不漏）——票据矩阵行 8 的服务端侧契约。
    /// </summary>
    [Fact]
    public void LargeStream_PagesInDefaultWindows_WithoutSkippingOrDuplicating()
    {
        const int reportCount = 2400;
        var stream = new List<StoredEvent>(capacity: reportCount + 1)
        {
            Assignment(1, 1, "vortox"),
        };
        for (var index = 0; index < reportCount; index++)
        {
            stream.Add(new StoredEvent
            {
                Sequence = index + 2,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatStateChangedEvent
                {
                    Seat = new SeatId(2),
                    Poison = index % 2 == 0 ? PoisonState.Poisoned : PoisonState.Healthy,
                    Reason = "测试：大事件流中毒切换",
                },
            });
        }

        var collected = new List<long>();
        var afterSequence = 0L;
        while (true)
        {
            var page = ReplayProjection.Build(stream, afterSequence, ReplayProjection.DefaultPageSize);
            collected.AddRange(page.Steps.Select(step => step.Sequence));
            if (!page.HasMore)
            {
                Assert.True(page.Steps.Count <= ReplayProjection.DefaultPageSize);
                break;
            }

            Assert.Equal(ReplayProjection.DefaultPageSize, page.Steps.Count);
            afterSequence = page.Steps[^1].Sequence;
        }

        Assert.True(collected.Count >= 2000, $"分页总步数只有 {collected.Count}");
        Assert.Equal(stream.Select(stored => stored.Sequence).ToArray(), collected.ToArray());
        Assert.Equal(collected.OrderBy(sequence => sequence).ToArray(), collected.ToArray());
    }

    /// <summary>
    /// 自指归因的死亡（方古侵染时原方古死亡，<c>CausedBy</c> = 自己）不是「被恶魔击杀」：
    /// 只画死亡帷幕，不画红色箭头——否则回放会把"恶魔离场"读成"恶魔刀了自己"。
    /// </summary>
    [Fact]
    public void DemonSelfDeath_HasNoKillArrow()
    {
        var stream = new List<StoredEvent>
        {
            Assignment(1, 3, "fang-gu"),
            new()
            {
                Sequence = 2,
                RecordedAt = DateTimeOffset.UnixEpoch,
                Event = new SeatStateChangedEvent
                {
                    Seat = new SeatId(3),
                    Life = LifeState.Dead,
                    Reason = "测试：方古侵染，原方古死亡",
                    CausedBy = new SeatId(3),
                },
            },
        };

        var step = ReplayProjection.Build(stream, 0, 100).Steps.Single(item => item.Sequence == 2);

        Assert.Contains(step.Markers, marker => marker.Kind == "shroud" && marker.Seat == new SeatId(3));
        Assert.DoesNotContain(step.Markers, marker => marker.Kind == "kill-arrow");
    }
}
