using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 白天阶段在真实宿主里的行为：开白天 → 提名 / 投票 / 计票 → 结束并处决 → 进入下一夜。
/// </summary>
/// <remarks>
/// 依据百科《规则概要》三 /《提名》/《投票》/《处决》· 2026-10-01 抓取与 R-0017；
/// 跑的是真宿主 + 真 SignalR + 真 SQLite。夹具夜晚（<see cref="TestNightPlan"/>）只用来把步骤机
/// 推到"夜晚已完成"；白天契约闸另有用例覆盖。
/// </remarks>
public sealed class DayPhaseHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static SeatCharacterAssignmentDto[] DayNeutralAssignments() =>
    [
        new() { Seat = 1, Character = "dreamer" },
        new() { Seat = 2, Character = "clockmaker" },
        new() { Seat = 3, Character = "no-dashii" },
    ];

    /// <summary>
    /// 完整白天流程的席位（4 席）：处决 1 人后仍剩 3 人存活——3 席夹具在处决后会
    /// **真实触发「仅剩两名存活 → 邪恶获胜」**，那是规则正确行为，不是测试想要覆盖的链路
    /// （呆瓜只坐在第 4 席、本用例中不会死亡）。
    /// </summary>
    private static SeatCharacterAssignmentDto[] FullDayAssignments() =>
    [
        .. DayNeutralAssignments(),
        new() { Seat = 4, Character = "klutz" },
    ];

    /// <summary>用宿主身份开一个夹具夜晚，再逐槽强推到完成（把状态推进到"可以开白天"）。</summary>
    private static async Task CompleteFixtureNightAsync(TestServerHost host, GameClient storyteller)
    {
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(3) },
            "test-day-fixture-night",
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
                $"test-day-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夹具夜晚在 12 次强推内没有走完");
    }

    /// <summary>完整走一遍白天：提名 → 两人投票 → 计票 → 结束并处决 → 开下一夜。</summary>
    [Fact]
    public async Task FullDayFlow_NominateVoteCountExecute_ThenNextNightCanStart()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 4, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FullDayAssignments(),
            "test-day-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await CompleteFixtureNightAsync(host, storyteller);

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-start");
        Assert.Equal("Accepted", started.Kind);

        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Phase == "Day" && candidate.Day is { Status: "Open", DayNumber: 1 },
            Wait);
        Assert.NotNull(view);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        // 1 号（存活）提名 2 号 → 说书人点「开始」进入钟盘收票（R-0017 目标形态）。
        var nominated = await seat1.InvokeAsync<CommandResultDto>("Nominate", 2, "test-day-nominate");
        Assert.Equal("Accepted", nominated.Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-day-sweep");

        // 2 号可以为自己举手（百科《规则概要》三-2）；3 号也举 → 收票后 2 票达到 3 名存活的一半以上。
        var voteSelf = await seat2.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-day-vote-self");
        Assert.Equal("Accepted", voteSelf.Kind);
        var voteOther = await seat3.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-day-vote-other");
        Assert.Equal("Accepted", voteOther.Kind);

        // 控制面按座次走完一圈：每席在分针指向它的那一刻冻结举手状态（先举也算、过时不候）。
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 4, "test-day-sweep");

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-day-count");
        Assert.Equal("Accepted", counted.Kind);

        view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day?.AboutToBeExecuted == 2,
            Wait);
        Assert.Equal(2, view!.Day!.AboutToBeExecuted);
        Assert.Equal("Counted", view.Day.Nominations.Single().Status);
        Assert.Equal(2, view.Day.Nominations.Single().Votes);

        // 结束白天：处决当前「即将被处决」的 2 号。
        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-day-close");
        Assert.Equal("Accepted", closed.Kind);

        view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { Status: "Closed", Executed: 2 },
            Wait);
        Assert.NotNull(view);
        var seat2Entry = Assert.Single(view!.Seats, entry => entry.Seat == 2);
        Assert.Contains(seat2Entry.Facts, fact => fact.Dimension == "Life" && fact.Value == "Dead");

        // 行 2 / 行 3（R-0022）：处决致死进入全体玩家的公开生死面，且死亡与处决是两条分开的事实；
        // 被处决者自己的界面显式可见、权限位随之更新。逐席位公开面一致、权限位各人自己。
        var seatOneDay = host.Session.GetPlayerView(new SeatId(1)).Day;
        var seatTwoDay = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(seatOneDay);
        Assert.NotNull(seatTwoDay);
        Assert.Equal(new SeatId(2), seatOneDay!.PublicView.Executed);
        Assert.Contains(seatOneDay.Lives, entry => entry.Seat.Value == 2 && entry.State == LifeState.Dead);
        Assert.Contains(seatOneDay.Announcements, entry => entry.Seat.Value == 2 && entry.State == LifeState.Dead);
        Assert.Equal(seatOneDay.Lives, seatTwoDay!.Lives);
        Assert.Equal(seatOneDay.Announcements, seatTwoDay.Announcements);
        Assert.False(seatTwoDay.CanNominate);

        // 行 4：重连包（快照 + 补齐）与在线投影同源——公告面不回退、不丢。
        var bundle = await host.Session.GetReconnectBundleAsync(new SeatId(1), 0, CancellationToken.None);
        Assert.NotNull(bundle.View.Day);
        Assert.Equal(seatOneDay.Lives, bundle.View.Day!.Lives);
        Assert.Equal(seatOneDay.Announcements, bundle.View.Day.Announcements);

        // 白天结束后可以开下一夜；白天账跨阶段保留（卖花女孩 / 城镇公告员要读）。
        var nextNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-day-next-night");
        Assert.Equal("Accepted", nextNight.Kind);

        view = await TestServerHost.WaitForViewAsync(storyteller, candidate => candidate.Phase == "OtherNight", Wait);
        Assert.NotNull(view);
        Assert.Equal("Closed", view!.Day?.Status);
        Assert.Equal(2, view.Day?.Executed);
    }

    /// <summary>
    /// 白天进行中重启宿主：白天账（提名 / 收票进度 / 举手）与阶段从事件流恢复；
    /// 未收完的收票**不追补**（中断等说书人继续），重启后还能把这天打完。
    /// </summary>
    [Fact]
    public async Task RestartDuringDay_RestoresDayLedgerAndCanContinue()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-test-day-restart-{Guid.NewGuid():N}.db");
        try
        {
            await using (var host = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 3,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false))
            {
                await using var storyteller = await host.ConnectStorytellerAsync();
                var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                    "AssignCharacters",
                    DayNeutralAssignments(),
                    "test-day-restart-assign");
                Assert.Equal("Accepted", assigned.Kind);

                await CompleteFixtureNightAsync(host, storyteller);

                var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-restart-start");
                Assert.Equal("Accepted", started.Kind);

                await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
                await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

                var nominated = await seat1.InvokeAsync<CommandResultDto>("Nominate", 2, "test-day-restart-nominate");
                Assert.Equal("Accepted", nominated.Kind);
                await VoteSweepTestDriver.StartAsync(host, 1, "test-day-restart-sweep");
                var voteNominee = await seat1.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-day-restart-vote-1");
                Assert.Equal("Accepted", voteNominee.Kind);
                var voteOther = await seat3.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-day-restart-vote-3");
                Assert.Equal("Accepted", voteOther.Kind);
            }

            await using (var restarted = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 3,
                databasePath: databasePath,
                deleteDatabaseOnDispose: true,
                autoStartTestNight: false))
            {
                // 重启只靠事件流恢复：阶段、白天账（进行中）、提名、收票进度与举手都在。
                var view = restarted.Session.GetStorytellerView();
                Assert.Equal("Day", view.Phase?.ToString());
                Assert.Equal(DayStatus.Open, view.Day?.Status);
                var nomination = Assert.Single(view.Day!.Nominations);
                Assert.Equal(NominationStatus.Voting, nomination.Status);
                Assert.Equal(1, nomination.Index);
                Assert.Equal(2, nomination.HandsRaised.Count);
                Assert.Empty(nomination.Ballot);

                // 未收完的收票不追补：重启后是「已中断」，等说书人显式继续（R-0017 目标形态）。
                Assert.Equal("Interrupted", view.VoteSweep?.Phase);

                // 还能继续打完：继续收票 → 按座次收完 → 计票 → 结束并处决。
                await using var storyteller = await restarted.ConnectStorytellerAsync();
                await VoteSweepTestDriver.ResumeAsync(restarted, 1, "test-day-restart-resume");
                await VoteSweepTestDriver.CollectAllAsync(restarted, 1, 3, "test-day-restart-collect");
                var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-day-restart-count");
                Assert.Equal("Accepted", counted.Kind);
                var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-day-restart-close");
                Assert.Equal("Accepted", closed.Kind);

                var after = restarted.Session.GetStorytellerView();
                Assert.Equal(DayStatus.Closed, after.Day?.Status);
                Assert.Equal(new SeatId(2), after.Day?.Executed);
            }
        }
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>
    /// 白天命令的身份闸（矩阵第 7 行的负向面）：玩家不能开白天 / 计票 / 结束并处决；
    /// 说书人不能提名 / 投票（玩家专属）——两个方向都是显式拒绝，而不是靠空席位崩溃。
    /// </summary>
    [Fact]
    public async Task DayCommands_EnforceIdentityGateInBothDirections()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            DayNeutralAssignments(),
            "test-day-identity-assign");
        Assert.Equal("Accepted", assigned.Kind);
        await CompleteFixtureNightAsync(host, storyteller);

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-identity-start");
        Assert.Equal("Accepted", started.Kind);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        // 开阶段是宿主 / 说书人专属；计票与结束白天是说书人专属。
        var playerStartDay = await seat1.InvokeAsync<CommandResultDto>("StartDay", "test-day-identity-player-start");
        Assert.Equal("identity.host_only", playerStartDay.RejectionCode);
        var playerCount = await seat1.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-day-identity-player-count");
        Assert.Equal("identity.storyteller_only", playerCount.RejectionCode);
        var playerClose = await seat1.InvokeAsync<CommandResultDto>("CloseDay", "test-day-identity-player-close");
        Assert.Equal("identity.storyteller_only", playerClose.RejectionCode);

        var storytellerNominate = await storyteller.InvokeAsync<CommandResultDto>(
            "Nominate",
            2,
            "test-day-identity-st-nominate");
        Assert.Equal("identity.player_only", storytellerNominate.RejectionCode);
        var storytellerVote = await storyteller.InvokeAsync<CommandResultDto>(
            "CastVote",
            1,
            true,
            "test-day-identity-st-vote");
        Assert.Equal("identity.player_only", storytellerVote.RejectionCode);

        // 全天零副作用：白天仍开着、没有提名、没有处决。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal("Day", view.Phase);
        Assert.Equal("Open", view.Day?.Status);
        Assert.Empty(view.Day!.Nominations);
        Assert.Null(view.Day.Executed);

        Assert.Contains(
            host.Logs,
            line => line.Contains("identity.player_only", StringComparison.Ordinal));
    }

    /// <summary>
    /// 矩阵第 7 行的正向证据：白天公开事实**逐席位一致**（只有自己的权限位不同）；
    /// 收票期间的举手 / 已收票公开是 R-0017 第 5 条登记的口径（线下举手本来就人人可见）。
    /// </summary>
    [Fact]
    public async Task PlayerDayProjection_IsPublicOnlyAndIdenticalAcrossSeats()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            DayNeutralAssignments(),
            "test-day-view-assign");
        Assert.Equal("Accepted", assigned.Kind);
        await CompleteFixtureNightAsync(host, storyteller);

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-view-start");
        Assert.Equal("Accepted", started.Kind);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));

        var nominated = await seat1.InvokeAsync<CommandResultDto>("Nominate", 2, "test-day-view-nominate");
        Assert.Equal("Accepted", nominated.Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-day-view-sweep");
        var voted = await seat3.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-day-view-vote");
        Assert.Equal("Accepted", voted.Kind);

        var seatOne = host.Session.GetPlayerView(new SeatId(1)).Day;
        var seatThree = host.Session.GetPlayerView(new SeatId(3)).Day;
        Assert.NotNull(seatOne);
        Assert.NotNull(seatThree);

        // 公开事实逐字段一致：天数 / 状态 / 提名 / 举手 / 收票进度 / 开放提名 / 候选名单。
        Assert.Equal(seatOne!.PublicView.DayNumber, seatThree!.PublicView.DayNumber);
        Assert.Equal(seatOne.PublicView.Status, seatThree.PublicView.Status);
        Assert.Equal(seatOne.PublicView.Nominations.Count, seatThree.PublicView.Nominations.Count);
        Assert.Equal(seatOne.PublicView.Nominations[0].Nominator, seatThree.PublicView.Nominations[0].Nominator);
        Assert.Equal(seatOne.PublicView.Nominations[0].Nominee, seatThree.PublicView.Nominations[0].Nominee);
        Assert.Equal(seatOne.PublicView.Nominations[0].HandsRaised, seatThree.PublicView.Nominations[0].HandsRaised);
        Assert.Equal(seatOne.VoteSweep?.Phase, seatThree.VoteSweep?.Phase);
        Assert.Equal(seatOne.PublicView.OpenNomination?.Index, seatThree.PublicView.OpenNomination?.Index);
        Assert.Equal(seatOne.PublicView.AboutToBeExecuted, seatThree.PublicView.AboutToBeExecuted);
        Assert.Equal(seatOne.NominationCandidates, seatThree.NominationCandidates);

        // 收票期间举手公开（R-0017 第 5 条）：3 号的举手立刻出现在两个席位的公开事实里；
        // 票面（冻结结论）要等逐席收票才产生。
        Assert.Single(seatOne.PublicView.Nominations[0].HandsRaised);
        Assert.Equal(new SeatId(3), seatOne.PublicView.Nominations[0].HandsRaised[0]);
        Assert.Empty(seatOne.PublicView.Nominations[0].Ballot);
        Assert.Equal("Countdown", seatOne.VoteSweep?.Phase);
        Assert.Null(seatOne.PublicView.AboutToBeExecuted);

        // 权限位是"自己的"：投过票的 3 号 voted=true；1 号是提名者、没投票。
        Assert.True(seatThree.Voted);
        Assert.False(seatOne.Voted);
        Assert.True(seatOne.CanVote);
        Assert.False(seatOne.CanNominate);

        // 候选名单只由公开规则派生：2 号已被提名 → 剩下 1 / 3 号可被提名（含自己，R-0018 暂取允许）。
        Assert.Equal(new[] { 1, 3 }, seatOne.NominationCandidates.Select(seat => seat.Value));
    }

    /// <summary>白天只能跟在夜晚之后：还没有任何阶段时开白天被拒绝。</summary>
    [Fact]
    public async Task StartDay_BeforeAnyNight_IsRejected()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-too-early");
        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("phase.day_requires_night", result.RejectionCode);
    }

    /// <summary>与白天相关、但契约未实现的角色在场 → 开白天显式拒绝，不静默跳过。</summary>
    /// <remarks>
    /// 用还没实现的**杂耍艺人**（博学者已随 R-0057 落地；两者都在白天的"未覆盖"名单里进出，
    /// 这条测试跟着名单走）。
    /// </remarks>
    [Fact]
    public async Task StartDay_WithUnimplementedDayRelevantCharacter_IsRejected()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "dreamer" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "juggler" },
            },
            "test-day-assign-juggler");
        Assert.Equal("Accepted", assigned.Kind);

        await CompleteFixtureNightAsync(host, storyteller);

        var result = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-day-juggler");
        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("legality.day_contract_missing", result.RejectionCode);
    }

    /// <summary>
    /// 零信任矩阵行 5 的原场景：**白天**提交夜间行动 → 阶段闸拒绝（不泄露、不带序号、状态不变、有审计）。
    /// </summary>
    [Fact]
    public async Task ZeroTrustRow5_SubmitNightActionDuringDay_IsRejectedByPhaseGate()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            DayNeutralAssignments(),
            "test-row5-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await CompleteFixtureNightAsync(host, storyteller);

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-row5-start");
        Assert.Equal("Accepted", started.Kind);

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        // 伪造一个"夜间行动"（没有挂起请求）：期望被阶段闸拒绝。
        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            "night-request-forged",
            "seat:2",
            "test-row5-submit",
            0L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("phase.no_request_for_you", result.RejectionCode);
        Assert.Equal(0, result.Sequence);
        Assert.Contains(
            host.Logs,
            line => line.Contains("phase.no_request_for_you", StringComparison.Ordinal));

        // 状态不变：白天仍开着、没有任何副作用。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal("Day", view.Phase);
        Assert.Equal("Open", view.Day?.Status);
        Assert.Null(view.Day?.AboutToBeExecuted);
    }
}
