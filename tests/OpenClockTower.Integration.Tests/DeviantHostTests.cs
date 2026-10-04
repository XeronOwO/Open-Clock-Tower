using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 怪咖免死的真宿主链路（票据 `traveller-and-exile` · D3 / R-0048）：
/// 流放达线 → 未裁定显式拒绝 → 说书人裁定 → 受保护存活 / 不受保护死亡；
/// 受理时机、重复裁定与重启恢复；跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 断言落在**事件流**与**公开投影**上——它们是重启 / 重连 / 复盘共同重建的事实来源（D-0010）；
/// 规则来源见百科《怪咖》· 2026-10-04 抓取 与 `docs/standard/rulings.md` R-0048。
/// </remarks>
public sealed class DeviantHostTests
{
    [Fact]
    public async Task DeviantExile_RequiresRuling_ThenProtectedSurvives()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithDeviantAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        await ProposeSweepAndVoteAsync(host, session, seat1, seat2, seat3);

        // 达线但还没裁定：计票显式拒绝——不猜、不静默死亡（R-0048 第 2 条）。
        var blocked = await storyteller.InvokeAsync<CommandResultDto>(
            "CountExileVotes",
            1,
            "test-deviant-count-blocked");
        Assert.Equal("Rejected", blocked.Kind);
        Assert.Equal("day.exile_protection_required", blocked.RejectionCode);

        // 说书人裁定「今天很有趣」→ 受保护。
        var ruled = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            session.TravellerSeat.Value,
            true,
            "说书人：今天怪咖很有趣",
            "test-deviant-rule");
        Assert.Equal("Accepted", ruled.Kind);

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-deviant-count");
        Assert.Equal("Accepted", counted.Kind);

        // 事件流：裁定事件 + 「受保护」结论；没有配套死亡事实（保护期间不死亡、不触发，R-0045 第 3 条）。
        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var ruledEvent = Assert.Single(events.Select(item => item.Event).OfType<DayProtectionDecidedEvent>());
        Assert.Equal(session.TravellerSeat, ruledEvent.Seat);
        Assert.True(ruledEvent.Protected);
        var countedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
        Assert.Equal(ExileConclusion.Protected, countedEvent.Conclusion);
        Assert.DoesNotContain(
            events.Select(item => item.Event).OfType<SeatStateChangedEvent>(),
            item => item.Seat == session.TravellerSeat && item.Life == LifeState.Dead);

        // 公开面：目标存活、流放结清；白天可以正常关闭。
        var view = host.Session.GetPlayerView(new SeatId(1));
        var exile = Assert.Single(view.Day!.PublicView.Exiles);
        Assert.Equal(ExileStatus.Counted, exile.Status);
        Assert.Equal(ExileConclusion.Protected, exile.Conclusion);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-deviant-close")).Kind);
    }

    [Fact]
    public async Task DeviantExile_UnprotectedRulingKillsTheTarget()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithDeviantAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        await ProposeSweepAndVoteAsync(host, session, seat1, seat2, seat3);

        var ruled = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            session.TravellerSeat.Value,
            false,
            "说书人：今天怪咖不够有趣",
            "test-deviant-rule");
        Assert.Equal("Accepted", ruled.Kind);

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-deviant-count");
        Assert.Equal("Accepted", counted.Kind);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var countedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
        Assert.Equal(ExileConclusion.Exiled, countedEvent.Conclusion);
        var death = Assert.Single(
            events.Select(item => item.Event).OfType<SeatStateChangedEvent>(),
            item => item.Seat == session.TravellerSeat && item.Life == LifeState.Dead);
        Assert.Equal(ExileMachine.ExileDeathReason, death.Reason);
    }

    [Fact]
    public async Task DeviantRuling_IsOnlyAcceptedAtTheDecidingMoment()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithDeviantAsync(host);
        var storyteller = session.Storyteller;
        var seat = session.TravellerSeat.Value;

        // 还没有任何流放：不受理（不提前问、不预缓存）。
        var tooEarly = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            seat,
            true,
            null,
            "test-deviant-early");
        Assert.Equal("Rejected", tooEarly.Kind);
        Assert.Equal("day.protection_not_required", tooEarly.RejectionCode);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        var proposed = await seat1.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            seat,
            "test-deviant-propose");
        Assert.Equal("Accepted", proposed.Kind);

        // 收票没走完：不受理。
        await ExileSweepTestDriver.StartAsync(host, 1, "test-deviant-sweep");
        var beforeSweep = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            seat,
            true,
            null,
            "test-deviant-before-sweep");
        Assert.Equal("Rejected", beforeSweep.Kind);
        Assert.Equal("day.protection_not_required", beforeSweep.RejectionCode);

        // 收完但只有 1 票（6 席的过半线 = 3）：不受理。
        var firstVote = await seat1.InvokeAsync<CommandResultDto>("CastExileVote", 1, true, "test-deviant-vote-1");
        Assert.Equal("Accepted", firstVote.Kind);
        await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(1, 6), "test-deviant-sweep");
        var notReached = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            seat,
            true,
            null,
            "test-deviant-not-reached");
        Assert.Equal("Rejected", notReached.Kind);
        Assert.Equal("day.protection_not_required", notReached.RejectionCode);

        // 席位不在局：形状闸先拒绝。
        var unknownSeat = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDayProtection",
            99,
            true,
            null,
            "test-deviant-unknown-seat");
        Assert.Equal("Rejected", unknownSeat.Kind);
    }

    [Fact]
    public async Task DeviantRuling_SurvivesHostRestart_AndTheCountStillWorks()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-deviant-{Guid.NewGuid():N}.db");

        try
        {
            // 第一段：开白天 → 提议 → 收票达线 → 计票被拒（等裁定）。
            await using (var firstHost = new TestServerHost(
                seatCount: 5,
                autoStartTestNight: false,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false))
            {
                await using var firstSession = await SetUpDayWithDeviantAsync(firstHost);
                await using var seat1 = await firstHost.ConnectSeatAsync(new SeatId(1));
                await using var seat2 = await firstHost.ConnectSeatAsync(new SeatId(2));
                await using var seat3 = await firstHost.ConnectSeatAsync(new SeatId(3));

                await ProposeSweepAndVoteAsync(firstHost, firstSession, seat1, seat2, seat3);
                var blocked = await firstSession.Storyteller.InvokeAsync<CommandResultDto>(
                    "CountExileVotes",
                    1,
                    "test-deviant-count-blocked");
                Assert.Equal("Rejected", blocked.Kind);
                Assert.Equal("day.exile_protection_required", blocked.RejectionCode);
            }

            // 第二段：重启恢复（快照 + 事件流）后裁定 → 计票 → 受保护。
            await using var host = new TestServerHost(
                seatCount: 5,
                autoStartTestNight: false,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false);
            await using var storyteller = await host.ConnectStorytellerAsync();

            var restored = host.Session.GetPlayerView(new SeatId(1));
            var exile = Assert.Single(restored.Day!.PublicView.Exiles);
            Assert.Equal(ExileStatus.Voting, exile.Status);

            var ruled = await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDayProtection",
                exile.Target.Value,
                true,
                "重启后裁定：今天很有趣",
                "test-deviant-rule-after-restart");
            Assert.Equal("Accepted", ruled.Kind);

            var counted = await storyteller.InvokeAsync<CommandResultDto>(
                "CountExileVotes",
                1,
                "test-deviant-count-after-restart");
            Assert.Equal("Accepted", counted.Kind);

            var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            Assert.Equal(
                ExileConclusion.Protected,
                events.Select(item => item.Event).OfType<ExileVoteCountedEvent>().Single().Conclusion);
        }
        finally
        {
            // 本用例刻意不复用宿主的清库开关（跨两个宿主）：自己产生的临时库自己清。
            DeleteDatabaseFile(databasePath);
        }
    }

    /// <summary>提议 → 开始慢节奏收票 → 1 / 2 / 3 号举手（6 席的过半线 = 3）→ 逐席收完。</summary>
    private static async Task ProposeSweepAndVoteAsync(
        TestServerHost host,
        DeviantSession session,
        params GameClient[] voters)
    {
        var proposed = await voters[0].InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-deviant-propose");
        Assert.Equal("Accepted", proposed.Kind);

        await ExileSweepTestDriver.StartAsync(host, 1, "test-deviant-sweep");
        for (var index = 0; index < voters.Length; index++)
        {
            var vote = await voters[index].InvokeAsync<CommandResultDto>(
                "CastExileVote",
                1,
                true,
                $"test-deviant-vote-{index}");
            Assert.Equal("Accepted", vote.Kind);
        }

        await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(1, 6), "test-deviant-sweep");
    }

    /// <summary>5 席固定角色（覆盖四类型；白天契约都已实现，旅行者单独加入）。</summary>
    private static SeatCharacterAssignmentDto[] FiveAssignments() =>
    [
        new() { Seat = 1, Character = "clockmaker" },
        new() { Seat = 2, Character = "dreamer" },
        new() { Seat = 3, Character = "artist" },
        new() { Seat = 4, Character = "klutz" },
        new() { Seat = 5, Character = "no-dashii" },
    ];

    /// <summary>开一个带 1 名怪咖的白天：分配 5 席 → 追加旅行者席位 → 走完夹具夜晚 → 开白天。</summary>
    private static async Task<DeviantSession> SetUpDayWithDeviantAsync(TestServerHost host)
    {
        var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FiveAssignments(),
            "test-deviant-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-deviant-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = new SeatId(joined.IssuedSeat!.Value);

        await CompleteFixtureNightAsync(host, storyteller, travellerSeat.Value);
        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-deviant-start-day");
        Assert.Equal("Accepted", started.Kind);
        return new DeviantSession(storyteller, travellerSeat);
    }

    /// <summary>用宿主身份开夹具夜晚，再逐槽强推到完成（把状态推进到"可以开白天"）。</summary>
    private static async Task CompleteFixtureNightAsync(TestServerHost host, GameClient storyteller, int seatCount)
    {
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
            "test-deviant-fixture-night",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：走完夹具夜晚",
                $"test-deviant-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夹具夜晚在 12 次强推内没有走完");
    }

    /// <summary>连续席位号（含两端）：流放收票按座次快照逐席走完。</summary>
    private static SeatId[] Seats(int first, int last) =>
        [.. Enumerable.Range(first, last - first + 1).Select(value => new SeatId(value))];

    private static void DeleteDatabaseFile(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>一次用例内持有的说书人客户端与怪咖席位（用例收尾时释放客户端）。</summary>
    private sealed record DeviantSession(GameClient Storyteller, SeatId TravellerSeat) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Storyteller.DisposeAsync();
    }
}
