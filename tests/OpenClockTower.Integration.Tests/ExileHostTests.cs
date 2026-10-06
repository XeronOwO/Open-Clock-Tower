using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 流放流程的真宿主链路（票据 `traveller-and-exile` · D2）：流放提议 / 钟盘收票 / 计票死亡，
/// 以及钟盘串行、死者票权、离场分母、重启恢复与契约护栏；跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《旅行者》· 2026-10-04 抓取 与 `docs/standard/rulings.md` R-0044 / R-0045；
/// 平台实施口径（钟盘串行、分母快照、拒绝码）见票据「D2 实施口径」。断言尽量落在**事件流**与
/// **公开投影**上——二者是重启 / 重连 / 复盘共同重建的事实来源（D-0010）。
/// </remarks>
public sealed class ExileHostTests
{
    [Fact]
    public async Task ExileDuringNomination_SerializesOnTheDial_ThenKillsTheTraveller()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        // 提名 1 → 2，并开始钟盘收票（先不收完）：白天里同时存在"开放中的提名"。
        var nominated = await seat1.InvokeAsync<CommandResultDto>("Nominate", 2, "test-exile-nominate");
        Assert.Equal("Accepted", nominated.Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-exile-nomination-sweep");

        // 提名收票进行中：任意玩家（含死者）都能发起流放提议（R-0044 第 2 条）——提案只是登记。
        var proposed = await seat3.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-propose");
        Assert.Equal("Accepted", proposed.Kind);

        // 钟盘串行：提名收票没走完，流放收票开不了（显式拒绝，票据「D2 实施口径」）。
        var blocked = await storyteller.InvokeAsync<CommandResultDto>(
            "StartExileSweep",
            1,
            ExileSweepTestDriver.SlowCountdownMilliseconds,
            ExileSweepTestDriver.SlowIntervalMilliseconds,
            "test-exile-sweep-blocked");
        Assert.Equal("Rejected", blocked.Kind);
        Assert.Equal("day.ballot_in_progress", blocked.RejectionCode);

        // 提名收票收完（还没计票）→ 不占钟盘：流放收票可以开始，提名稍后再计票。
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 6, "test-exile-nomination-sweep");
        await ExileSweepTestDriver.StartAsync(host, 1, "test-exile-sweep");

        // 三名在局玩家举手赞成：6 席的过半线 = 3 票（R-0044 第 5 条，奇数上取整）。
        var voters = new[] { seat1, seat2, seat3 };
        for (var index = 0; index < voters.Length; index++)
        {
            var vote = await voters[index].InvokeAsync<CommandResultDto>(
                "CastExileVote",
                1,
                true,
                $"test-exile-vote-{index}");
            Assert.Equal("Accepted", vote.Kind);
        }

        await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(1, 6), "test-exile-sweep");
        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-exile-count");
        Assert.Equal("Accepted", counted.Kind);

        // 事件流是唯一事实来源：计票结论 + 流放死亡（reason = day.exile）。
        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var countedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
        Assert.Equal(ExileConclusion.Exiled, countedEvent.Conclusion);
        Assert.Equal(3, countedEvent.Voters.Count);
        var death = Assert.Single(
            events.Select(item => item.Event).OfType<SeatStateChangedEvent>(),
            item => item.Seat == session.TravellerSeat
                && item.Life == LifeState.Dead
                && item.Reason == ExileMachine.ExileDeathReason);
        Assert.Equal(session.TravellerSeat, death.Seat);

        // 阈值分母 = 开始收票时的在局座次快照（6 席；R-0044 第 6 条）。
        var startedEvent = Assert.Single(
            events.Select(item => item.Event).OfType<ExileSweepStartedEvent>());
        Assert.Equal(6, startedEvent.Seats.Count);

        // 公开面：白天死亡即时公开（R-0045 第 2 条），流放账对玩家公开（R-0044 第 7 条）。
        var view = host.Session.GetPlayerView(new SeatId(1));
        Assert.Contains(
            view.Day!.Announcements,
            entry => entry.Seat == session.TravellerSeat && entry.State == LifeState.Dead);
        var exile = Assert.Single(view.Day!.PublicView.Exiles);
        Assert.Equal(ExileStatus.Counted, exile.Status);
        Assert.Equal(ExileConclusion.Exiled, exile.Conclusion);

        // 每名旅行者每个白天只能被提议一次（成败都算；R-0044 第 3 条）。
        var again = await seat3.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-propose-again");
        Assert.Equal("Rejected", again.Kind);
        Assert.Equal("day.exile_already_proposed", again.RejectionCode);

        // 提名链路继续收口：计票 → 结束白天（流放结清不阻断原有流程）。
        var nominationCount = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-exile-nomination-count");
        Assert.Equal("Accepted", nominationCount.Kind);
        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-exile-close-day");
        Assert.Equal("Accepted", closed.Kind);
    }

    [Fact]
    public async Task DeviantDayContract_IsCovered_AndTheDayStarts()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FiveAssignments(),
            "test-exile-contract-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 怪咖的「免死」在 D3 翻覆盖（R-0048）：带怪咖的局能开白天。收口的完整链路
        // （达线时裁定、受保护存活、不受保护死亡）见 DeviantHostTests。
        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-exile-contract-join");
        Assert.Equal("Accepted", joined.Kind);

        await CompleteFixtureNightAsync(host, storyteller, joined.IssuedSeat!.Value);

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-exile-contract-start-day");
        Assert.Equal("Accepted", started.Kind);
    }

    [Fact]
    public async Task Exile_DeadVoterDoesNotSpendTheToken_AndCanStillVoteInANomination()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        // 说书人上报 2 号死亡：流放表决里他仍可举手，且**不查也不耗**死后票权（R-0044 第 1 / 4 条）。
        var reported = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            2,
            "Dead",
            null,
            null,
            null,
            null,
            "test.exile.death",
            null,
            "test-exile-report-dead");
        Assert.Equal("Accepted", reported.Kind);

        var proposed = await seat1.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-propose");
        Assert.Equal("Accepted", proposed.Kind);
        await ExileSweepTestDriver.StartAsync(host, 1, "test-exile-sweep");

        var deadVote = await seat2.InvokeAsync<CommandResultDto>("CastExileVote", 1, true, "test-exile-vote-dead");
        Assert.Equal("Accepted", deadVote.Kind);
        var firstVote = await seat1.InvokeAsync<CommandResultDto>("CastExileVote", 1, true, "test-exile-vote-1");
        Assert.Equal("Accepted", firstVote.Kind);
        var thirdVote = await seat3.InvokeAsync<CommandResultDto>("CastExileVote", 1, true, "test-exile-vote-3");
        Assert.Equal("Accepted", thirdVote.Kind);

        await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(1, 6), "test-exile-sweep");
        var exileCounted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-exile-count");
        Assert.Equal("Accepted", exileCounted.Kind);

        // 死后票权还在：随后的一次提名里 2 号仍然投得出去（若流放已经把它花掉，这条会被
        // day.vote_token_spent 拒绝）；它只在**提名计票**时才被消耗（R-0017 第 4 条）。
        var nominated = await seat1.InvokeAsync<CommandResultDto>("Nominate", 3, "test-exile-nomination");
        Assert.Equal("Accepted", nominated.Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-exile-nomination-sweep");
        var nominationVote = await seat2.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-exile-nomination-vote-dead");
        Assert.Equal("Accepted", nominationVote.Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 6, "test-exile-nomination-sweep");
        var nominationCount = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-exile-nomination-count");
        Assert.Equal("Accepted", nominationCount.Kind);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var nominationCounted = Assert.Single(events.Select(item => item.Event).OfType<VoteCountedEvent>());
        Assert.Contains(new SeatId(2), nominationCounted.SpentVoteTokens);
        var exileCountedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
        Assert.Contains(new SeatId(2), exileCountedEvent.Voters);
        Assert.Equal(ExileConclusion.Exiled, exileCountedEvent.Conclusion);
    }

    [Fact]
    public async Task Exile_DenominatorExcludesDepartedSeats_AndTargetCannotLeaveMidExile()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        var storyteller = session.Storyteller;

        // 再追加一名旅行者（7 席在局），随后把他移出本局：流放分母只算在局的 6 席。
        var secondJoined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Good",
            null,
            "test-exile-join-second");
        Assert.Equal("Accepted", secondJoined.Kind);
        var secondTraveller = new SeatId(secondJoined.IssuedSeat!.Value);
        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            secondTraveller.Value,
            "测试：离场",
            "test-exile-remove-second");
        Assert.Equal("Accepted", removed.Kind);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        var proposed = await seat1.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-propose");
        Assert.Equal("Accepted", proposed.Kind);

        // 流放未结清时不能把目标移出本局：否则会出现"目标已离场却流放成立"（票据 D2 实施口径的离场闸）。
        var blockedRemove = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            session.TravellerSeat.Value,
            "测试：试图在流放中离场",
            "test-exile-remove-target");
        Assert.Equal("Rejected", blockedRemove.Kind);
        Assert.Equal("legality.traveller_exile_unsettled", blockedRemove.RejectionCode);

        await ExileSweepTestDriver.StartAsync(host, 1, "test-exile-sweep");
        var voters = new[] { seat1, seat2, seat3 };
        for (var index = 0; index < voters.Length; index++)
        {
            var vote = await voters[index].InvokeAsync<CommandResultDto>(
                "CastExileVote",
                1,
                true,
                $"test-exile-vote-{index}");
            Assert.Equal("Accepted", vote.Kind);
        }

        await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(1, 6), "test-exile-sweep");
        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-exile-count");
        Assert.Equal("Accepted", counted.Kind);

        // 3 票 = 6 席在局的一半 → 达线。若分母错误地含已离场的第 7 席（ceiling 7/2 = 4），本次不会成立。
        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var startedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileSweepStartedEvent>());
        Assert.Equal(6, startedEvent.Seats.Count);
        Assert.DoesNotContain(secondTraveller, startedEvent.Seats);
        var countedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
        Assert.Equal(ExileConclusion.Exiled, countedEvent.Conclusion);
    }

    [Fact]
    public async Task Exile_SurvivesHostRestart_AndResumesTheInterruptedSweep()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-exile-{Guid.NewGuid():N}.db");

        try
        {
            // 第一段：开白天 → 提议 → 开始收票 → 只收一席，留下未收完的收票。
            await using (var firstHost = new TestServerHost(
                seatCount: 5,
                autoStartTestNight: false,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false))
            {
                await using var firstSession = await SetUpDayWithTravellerAsync(firstHost);
                await using var firstSeat1 = await firstHost.ConnectSeatAsync(new SeatId(1));

                var firstProposed = await firstSeat1.InvokeAsync<CommandResultDto>(
                    "ProposeExile",
                    firstSession.TravellerSeat.Value,
                    "test-exile-propose");
                Assert.Equal("Accepted", firstProposed.Kind);
                await ExileSweepTestDriver.StartAsync(firstHost, 1, "test-exile-sweep");

                var firstCollect = await firstHost.ExecuteSystemCommandAsync(
                    new CollectExileSeatVoteCommand { ExileIndex = 1, Seat = new SeatId(1) },
                    "test-exile-sweep:seat-1",
                    CancellationToken.None);
                Assert.Equal(CommandResultKind.Accepted, firstCollect.Kind);
            }

            // 第二段：重启恢复（快照 + 事件流）；收票进度保留、锚点中断，等说书人继续。
            await using var host = new TestServerHost(
                seatCount: 5,
                autoStartTestNight: false,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false);
            var restored = host.Session.GetPlayerView(new SeatId(1));
            var exile = Assert.Single(restored.Day!.PublicView.Exiles);
            Assert.Equal(ExileStatus.Voting, exile.Status);
            var collectedSeat = Assert.Single(exile.Sweep!.Collected);
            Assert.Equal(new SeatId(1), collectedSeat.Seat);

            await using var storyteller = await host.ConnectStorytellerAsync();
            var resumed = await storyteller.InvokeAsync<CommandResultDto>("ResumeExileSweep", 1, "test-exile-resume");
            Assert.True(
                resumed.Kind == "Accepted",
                $"重启恢复后继续收票被拒：{resumed.RejectionCode} {resumed.RejectionMessage}");

            await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
            await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));
            await using var seat4 = await host.ConnectSeatAsync(new SeatId(4));
            var voters = new[] { seat2, seat3, seat4 };
            for (var index = 0; index < voters.Length; index++)
            {
                var vote = await voters[index].InvokeAsync<CommandResultDto>(
                    "CastExileVote",
                    1,
                    true,
                    $"test-exile-vote-{index}");
                Assert.Equal("Accepted", vote.Kind);
            }

            // 1 号在重启前已经收过票（冻结结论保留）：从 2 号接着收完剩下的席位。
            await ExileSweepTestDriver.CollectAllAsync(host, 1, Seats(2, 6), "test-exile-sweep-after-restart");
            var counted = await storyteller.InvokeAsync<CommandResultDto>("CountExileVotes", 1, "test-exile-count-after-restart");
            Assert.Equal("Accepted", counted.Kind);

            var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            var countedEvent = Assert.Single(events.Select(item => item.Event).OfType<ExileVoteCountedEvent>());
            Assert.Equal(ExileConclusion.Exiled, countedEvent.Conclusion);
        }
        finally
        {
            // 本用例刻意不复用宿主的清库开关（跨两个宿主）：自己产生的临时库自己清。
            DeleteDatabaseFile(databasePath);
        }
    }

    /// <summary>
    /// 控制面节拍器：流放收票按服务端时钟自动逐席推进（R-0017 目标形态；提名与流放共用节拍器）。
    /// </summary>
    [Fact]
    public async Task Exile_AutomaticPacer_CollectsTheFirstSeat()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        var proposed = await seat1.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-propose");
        Assert.Equal("Accepted", proposed.Kind);

        // 用最快节奏（1s / 0.3s）让真节拍器接棒；到点判定与生产完全同一条链路。
        var started = await session.Storyteller.InvokeAsync<CommandResultDto>(
            "StartExileSweep",
            1,
            1000,
            300,
            "test-exile-sweep-fast");
        Assert.Equal("Accepted", started.Kind);

        // 有界轮询等第一条逐席结论落库：第一席必然是快照里的 1 号（按席位升序）。
        ExileSeatVoteCollectedEvent? firstCollected = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (firstCollected is null && DateTime.UtcNow < deadline)
        {
            var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            firstCollected = events
                .Select(item => item.Event)
                .OfType<ExileSeatVoteCollectedEvent>()
                .FirstOrDefault();
            if (firstCollected is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(firstCollected);
        Assert.Equal(new SeatId(1), firstCollected!.Seat);
    }

    /// <summary>
    /// D7 投影面：流放的表态入口与服务端算好的**权限位 / 候选**走同一份 wire 投影
    /// （前端据此显示入口；真正的拒绝仍在服务端——D-0012 / R-0044）。
    /// </summary>
    [Fact]
    public async Task PlayerProjection_ExposesExilePermissionsAndCandidates()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        // 提议前：白天开放、没有未结清流放 → 可以发起；候选 = 在局旅行者（追加的 6 号）。
        var before = ProjectionMapper.ToDto(host.Session.GetPlayerView(new SeatId(1))).Day
            ?? throw new InvalidOperationException("白天投影应当可用");
        Assert.True(before.CanProposeExile);
        Assert.Equal(new[] { session.TravellerSeat.Value }, before.ExileCandidates);
        Assert.False(before.CanVoteExile);
        Assert.Null(before.PublicView.OpenExileIndex);

        var proposed = await seat1.InvokeAsync<CommandResultDto>(
            "ProposeExile",
            session.TravellerSeat.Value,
            "test-exile-projection-propose");
        Assert.Equal("Accepted", proposed.Kind);

        // 提议后、收票前：同日顺序进行 → 不能再提下一条；收票没开始 → 还不能举手。
        var proposedDay = ProjectionMapper.ToDto(host.Session.GetPlayerView(new SeatId(1))).Day
            ?? throw new InvalidOperationException("白天投影应当可用");
        Assert.False(proposedDay.CanProposeExile);
        Assert.False(proposedDay.CanVoteExile);
        Assert.Equal(1, proposedDay.PublicView.OpenExileIndex);
        Assert.Empty(proposedDay.ExileCandidates);

        // 收票开始：本席可举手；钟盘相位随投影下发（实时呈现与投影同源）。
        await ExileSweepTestDriver.StartAsync(host, 1, "test-exile-projection-sweep");
        var sweepDay = ProjectionMapper.ToDto(host.Session.GetPlayerView(new SeatId(1))).Day
            ?? throw new InvalidOperationException("白天投影应当可用");
        Assert.True(sweepDay.CanVoteExile);
        Assert.False(sweepDay.ExileSeatCollected);
        var openExile = Assert.Single(sweepDay.PublicView.Exiles);
        Assert.Equal("Countdown", openExile.Sweep?.Phase);
    }

    /// <summary>5 席固定角色（覆盖四类型；白天契约都已实现，不带旅行者夹具）。</summary>
    private static SeatCharacterAssignmentDto[] FiveAssignments() =>
    [
        new() { Seat = 1, Character = "clockmaker" },
        new() { Seat = 2, Character = "dreamer" },
        new() { Seat = 3, Character = "artist" },
        new() { Seat = 4, Character = "klutz" },
        new() { Seat = 5, Character = "no-dashii" },
    ];

    /// <summary>开一个带 1 名旅行者的白天：分配 5 席 → 追加旅行者席位 → 走完夹具夜晚 → 开白天。</summary>
    private static async Task<DaySession> SetUpDayWithTravellerAsync(TestServerHost host)
    {
        var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FiveAssignments(),
            "test-exile-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "barista",
            "Good",
            null,
            "test-exile-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = new SeatId(joined.IssuedSeat!.Value);

        await CompleteFixtureNightAsync(host, storyteller, travellerSeat.Value);
        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-exile-start-day");
        Assert.Equal("Accepted", started.Kind);
        return new DaySession(storyteller, travellerSeat);
    }

    /// <summary>用宿主身份开夹具夜晚，再逐槽强推到完成（把状态推进到"可以开白天"）。</summary>
    private static async Task CompleteFixtureNightAsync(TestServerHost host, GameClient storyteller, int seatCount)
    {
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
            "test-exile-fixture-night",
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
                $"test-exile-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夹具夜晚在 12 次强推内没有走完");
    }

    /// <summary>连续席位号（含两端）：流放收票按座次快照逐席走完。</summary>
    private static SeatId[] Seats(int first, int last) =>
        [.. Enumerable.Range(first, last - first + 1).Select(value => new SeatId(value))];

    private static void DeleteDatabaseFile(string databasePath) => TestDatabaseFiles.Delete(databasePath);

    /// <summary>一次用例内持有的说书人客户端与旅行者席位（用例收尾时释放客户端）。</summary>
    private sealed record DaySession(GameClient Storyteller, SeatId TravellerSeat) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Storyteller.DisposeAsync();
    }
}
