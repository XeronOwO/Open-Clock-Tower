using System.Collections.Concurrent;
using System.Text.Json;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 女巫在真实宿主里的完整链路（真宿主 + 真 SignalR + 真 SQLite + **真实**夜晚顺序表）：
/// 夜晚施加「被诅咒」→ 开白天 → 被诅咒者发起提名即死、提名仍然成立 → 诅咒的两种归宿；
/// 玩家端全程看不到这条说书人专属事实。
/// </summary>
/// <remarks>
/// <para>
/// 角色来源：百科《女巫》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记。
/// 存活人数必须 &gt; 3 女巫才保有这个能力，因此夹具用 4 席（女巫 + 三名已实现契约的角色）——
/// 这不是凑数：它就是「只剩三名存活玩家时你失去此能力」那条规则的边界条件。
/// </para>
/// <para>
/// 四席局有一个直接推论，本文件的第二条用例专门锁定它：诅咒一旦真的杀人，存活人数必然掉到 3，
/// 女巫当场失去能力 → 诅咒在同一批提交里解除（百科范例 4 同形）；**黄昏撤下**要在诅咒没被触发、
/// 存活仍 &gt; 3 的白天结束时才看得到。
/// </para>
/// <para>
/// 只走女巫自己的槽位，其余槽位由说书人强推越过（D-0014 兜底）——本用例判的是诅咒链路，
/// 不是钟表匠 / 筑梦师的信息链路（那两条各有自己的批次证据）。
/// </para>
/// </remarks>
public sealed class WitchCurseHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private const int WitchSeat = 1;

    private const int CursedSeat = 4;

    /// <summary>四席：1 号女巫 + 钟表匠 / 筑梦师 / 诺-达鲺（当前已实现契约的三名角色）。</summary>
    private static SeatCharacterAssignmentDto[] Assignments() =>
    [
        new() { Seat = 1, Character = "witch" },
        new() { Seat = 2, Character = "clockmaker" },
        new() { Seat = 3, Character = "dreamer" },
        new() { Seat = 4, Character = "no-dashii" },
    ];

    /// <summary>被诅咒者发起提名 → 他死、提名仍然成立；存活掉到 3 → 女巫当场失去能力、诅咒解除。</summary>
    [Fact]
    public async Task CursedNominator_DiesOnNomination_AndTheNominationStands()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 4, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var cursed = await OpenCursedDayAsync(host, storyteller);

        // 被诅咒者发起提名：受理、提名**仍然成立**，同一次提交里他死亡、且不是处决。
        var nominated = await cursed.InvokeAsync<CommandResultDto>("Nominate", 1, "test-witch-nominate");
        Assert.Equal("Accepted", nominated.Kind);

        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(entry => entry.Seat == 4
                && entry.Facts.Any(fact => fact.Dimension == "Life" && fact.Value == "Dead")),
            Wait);
        Assert.NotNull(view);

        var nomination = Assert.Single(view!.Day!.Nominations);
        Assert.Equal(4, nomination.Nominator);
        Assert.Equal(1, nomination.Nominee);
        Assert.Equal("Voting", nomination.Status);
        Assert.Equal(1, view.Day.OpenNominationIndex);
        Assert.Null(view.Day.AboutToBeExecuted);
        Assert.Null(view.Day.Executed);

        var death = Assert.Single(view.RecentSeatChanges, change => change.Seat == 4 && change.Life == "Dead");
        Assert.Equal(1, death.CausedBy);
        Assert.Contains("witch:curse", death.EffectId!, StringComparison.Ordinal);
        Assert.Contains("女巫", death.Reason, StringComparison.Ordinal);

        // 存活降到 3 → 女巫失去能力 → 诅咒在同一批提交里解除（TerminationKind = NoLongerApplies）。
        var removed = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Effects.Any(effect => effect.Ability == "witch.curse" && effect.Terminated),
            Wait);
        Assert.NotNull(removed);
        var curse = Assert.Single(removed!.Effects, effect => effect.Ability == "witch.curse");
        Assert.Equal("NoLongerApplies", curse.TerminationKind);
        Assert.Contains("失去", Assert.IsType<string>(curse.TerminationReason), StringComparison.Ordinal);

        // 提名仍然成立：还没计票之前白天根本关不掉——比"状态字段是 Voting"更硬的证据。
        var prematureClose = await storyteller.InvokeAsync<CommandResultDto>(
            "CloseDay",
            "test-witch-close-too-early");
        Assert.Equal("Rejected", prematureClose.Kind);
        Assert.Equal("day.nomination_not_counted", prematureClose.RejectionCode);

        // 计票：无人投票 → 不进入「即将被处决」；结束白天也不产生处决（诅咒致死不是处决）。
        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-witch-count");
        Assert.Equal("Accepted", counted.Kind);

        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-witch-close");
        Assert.Equal("Accepted", closed.Kind);

        var afterClose = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { Status: "Closed" },
            Wait);
        Assert.NotNull(afterClose);
        Assert.Null(afterClose!.Day!.Executed);
        Assert.Null(afterClose.Day.AboutToBeExecuted);

        // 同一次原子提交：提名 → 咒杀死亡 → 诅咒解除三条事件序号**连续**（派生事件与业务事件同批落库，
        // 重放只折事件、不重算，D-0010）。
        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        var nominationSequence = Assert.Single(
            stored,
            item => item.Event is NominationMadeEvent { Nominator.Value: CursedSeat }).Sequence;
        var deathSequence = Assert.Single(
            stored,
            item => item.Event is SeatStateChangedEvent { Seat.Value: CursedSeat, Life: LifeState.Dead }).Sequence;
        var terminationSequence = Assert.Single(
            stored,
            item => item.Event is PersistentEffectTerminatedEvent
            {
                EffectId.Value: "sv:night-1:witch:curse",
            }).Sequence;
        Assert.Equal(nominationSequence + 1, deathSequence);
        Assert.Equal(deathSequence + 1, terminationSequence);
    }

    /// <summary>
    /// 白天进行中重启宿主：诅咒随事件流与快照恢复，重启后被诅咒者提名**仍然**触发死亡。
    /// </summary>
    [Fact]
    public async Task RestartDuringCursedDay_KeepsTheCurseAndStillTriggers()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-test-witch-restart-{Guid.NewGuid():N}.db");
        await using (var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 4,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false,
            autoStartTestNight: false))
        {
            await using var storyteller = await host.ConnectStorytellerAsync();
            await using var cursed = await OpenCursedDayAsync(host, storyteller);

            // 重启前：白天开着、诅咒在账上且生效中。
            var before = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.Equal("Day", before.Phase);
            Assert.Contains(before.Effects, effect => effect.Ability == "witch.curse" && !effect.Terminated);
        }

        await using (var restarted = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 4,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true,
            autoStartTestNight: false))
        {
            await using var storyteller2 = await restarted.ConnectStorytellerAsync();
            var restored = await storyteller2.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.Equal("Day", restored.Phase);
            Assert.Contains(restored.Effects, effect => effect.Ability == "witch.curse" && !effect.Terminated);

            await using var cursed = await restarted.ConnectSeatAsync(new SeatId(CursedSeat));
            var nominated = await cursed.InvokeAsync<CommandResultDto>(
                "Nominate",
                WitchSeat,
                "test-witch-restart-nominate");
            Assert.Equal("Accepted", nominated.Kind);

            var after = await TestServerHost.WaitForViewAsync(
                storyteller2,
                candidate => candidate.Seats.Any(entry => entry.Seat == CursedSeat
                    && entry.Facts.Any(fact => fact.Dimension == "Life" && fact.Value == "Dead")),
                Wait);
            Assert.NotNull(after);
        }
    }

    /// <summary>诅咒没被触发时：白天一结束（黄昏）就按《女巫》提示标记的移除时机撤下。</summary>
    [Fact]
    public async Task UntriggeredCurse_IsRemovedAtDusk()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 4, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var cursed = await OpenCursedDayAsync(host, storyteller);

        // 白天没有任何提名：结束白天（无人被处决）。
        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-witch-dusk-close");
        Assert.Equal("Accepted", closed.Kind);

        var afterClose = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Effects.Any(effect => effect.Ability == "witch.curse" && effect.Terminated),
            Wait);
        Assert.NotNull(afterClose);

        var curse = Assert.Single(afterClose!.Effects, effect => effect.Ability == "witch.curse");
        Assert.Equal("NoLongerApplies", curse.TerminationKind);
        Assert.Contains("黄昏", Assert.IsType<string>(curse.TerminationReason), StringComparison.Ordinal);
        Assert.Equal("Closed", afterClose.Day!.Status);

        // 4 号活着、也没有被处决——死亡不是诅咒解除的原因，黄昏才是。
        Assert.Contains(
            afterClose.Seats.Single(entry => entry.Seat == 4).Facts,
            fact => fact.Dimension == "Life" && fact.Value == "Alive");
    }

    /// <summary>
    /// 开局分配 → 开首夜 → 女巫诅咒 4 号 → 越过其余槽位走完首夜 → 开白天；返回 4 号玩家客户端。
    /// </summary>
    private static async Task<GameClient> OpenCursedDayAsync(TestServerHost host, GameClient storyteller)
    {
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Assignments(),
            "test-witch-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var witch = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        var cursed = await host.ConnectSeatAsync(new SeatId(4));

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-witch-night");
        Assert.Equal("Accepted", started.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait), "女巫没有收到操作请求");

        var request = requests.First();
        Assert.Equal(1, request.Seat);
        Assert.Contains("女巫", request.Context, StringComparison.Ordinal);
        Assert.Equal(["seat:1", "seat:2", "seat:3", "seat:4"], request.Options.Select(option => option.Value));

        var answered = await witch.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            request.RequestId,
            "seat:4",
            "test-witch-answer",
            1L);
        Assert.Equal("Accepted", answered.Kind);

        // 诅咒进账：说书人视图里出现「被诅咒」持续型效果（作用对象 = 4 号），且尚未终止。
        var cursedView = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Effects.Any(effect => effect.Ability == "witch.curse"),
            Wait);
        Assert.NotNull(cursedView);
        var curse = Assert.Single(cursedView!.Effects, effect => effect.Ability == "witch.curse");
        Assert.Equal(1, curse.Source);
        Assert.Equal(4, curse.Target);
        Assert.Equal("witch", curse.SourceCharacter);
        Assert.False(curse.Terminated);

        await CompleteNightAsync(storyteller);

        var dayStarted = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-witch-day");
        Assert.Equal("Accepted", dayStarted.Kind);

        // 视角：玩家视图（wire 形状）里没有这条说书人专属事实——被诅咒者本人与女巫本人都不含。
        foreach (var seat in new[] { new SeatId(1), new SeatId(4) })
        {
            var wire = JsonSerializer.Serialize(ProjectionMapper.ToDto(host.Session.GetPlayerView(seat)));
            Assert.DoesNotContain("witch.curse", wire, StringComparison.Ordinal);
        }

        return cursed;
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本用例只需要女巫那一步被真正结算。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过与女巫无关的槽位",
                $"test-witch-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("首夜在 24 次强推内没有走完");
    }
}
