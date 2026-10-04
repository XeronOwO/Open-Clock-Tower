using System.Collections.Concurrent;
using System.Text.Json;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 疯狂与处罚处决在真实宿主里的完整链路（真宿主 + 真 SignalR + 真 SQLite + 真实夜晚顺序表）：
/// 洗脑师两维选择签发要求 → 目标知情 → 说书人白天处罚（占上限、立即入夜）→ 黎明到期；
/// 畸形秀演员的夜晚处罚不占次日上限。
/// </summary>
/// <remarks>
/// 口径：<c>docs/standard/rulings.md</c> R-0020 / R-0021；规则来源：百科《洗脑师》《畸形秀演员》
/// · 2026-10-01 抓取。四席 = 两名角色之一 + 当前已实现契约的其余角色（钟表匠 / 筑梦师 / 诺-达鲺）。
/// </remarks>
public sealed class MadnessPunishmentHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>洗脑师白天处罚：占当天上限、立即入夜；要求在下个黎明到期撤下。</summary>
    [Fact]
    public async Task CerenovusMadness_DayPunishment_ConsumesTheLimit_AndExpiresAtTheNextDawn()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var target = await OpenCerenovusDayAsync(host, storyteller);

        // 白天处罚 4 号：处决事实 + 死亡 + 关天（立即入夜）。
        var punished = await storyteller.InvokeAsync<CommandResultDto>(
            "PunishExecution",
            4,
            "Cerenovus",
            "测试：4 号未按疯狂要求行动",
            "test-madness-punish");
        Assert.Equal("Accepted", punished.Kind);

        var afterPunish = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(entry => entry.Seat == 4
                && entry.Facts.Any(fact => fact.Dimension == "Life" && fact.Value == "Dead")),
            Wait);
        Assert.NotNull(afterPunish);
        Assert.Equal("Closed", afterPunish!.Day!.Status);
        Assert.Equal(4, afterPunish.Day.Executed);

        // 夜晚照常开始；第二个黎明（第 2 天开始）要求到期撤下（R-0021）。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-madness-night-2");
        Assert.Equal("Accepted", nightTwo.Kind);
        await CompleteNightAsync(storyteller, "n2");

        var dayTwo = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-madness-day-2");
        Assert.Equal("Accepted", dayTwo.Kind);

        var expired = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { Status: "Open" }
                && candidate.Seats.Single(entry => entry.Seat == 4).Madnesses.Length == 0,
            Wait);
        Assert.NotNull(expired);

        // 事件流：签发 → 处罚处决（白天）→ 到期撤下，三类事实都在。
        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        Assert.Single(stored, item => item.Event is MadnessRequirementIssuedEvent
        {
            Requirement.Id.Value: "sv:night-1:cerenovus:madness",
        });
        Assert.Single(stored, item => item.Event is ExecutedEvent
        {
            Kind: ExecutionKind.CerenovusMadness,
            DayNumber: 1,
            Seat.Value: 4,
        });
        Assert.Single(stored, item => item.Event is MadnessRequirementTerminatedEvent
        {
            Id.Value: "sv:night-1:cerenovus:madness",
        });

        // 视角：说书人专属的要求字段与要求标识不进任何玩家投影；
        // 目标本人拿到的只有那条私密告知（含能力标识与要证明的角色，按《洗脑师》运作方式）。
        for (var seat = 1; seat <= 4; seat++)
        {
            var wire = JsonSerializer.Serialize(
                ProjectionMapper.ToDto(host.Session.GetPlayerView(new SeatId(seat))));
            Assert.DoesNotContain("madnesses", wire, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sv:night-1", wire, StringComparison.Ordinal);

            if (seat != 4)
            {
                Assert.DoesNotContain("cerenovus.madness", wire, StringComparison.Ordinal);
                Assert.DoesNotContain("钟表匠", wire, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>畸形秀演员夜晚处罚：当天白天已经处决过仍可处罚；**不占次日上限**（百科角色简介 6）。</summary>
    [Fact]
    public async Task Mutant_NightPunishment_DoesNotConsumeTheNextDay()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            MutantAssignments(),
            "test-mutant-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var two = await host.ConnectSeatAsync(new SeatId(2));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));
        await using var four = await host.ConnectSeatAsync(new SeatId(4));
        await using var five = await host.ConnectSeatAsync(new SeatId(5));

        var night = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-mutant-night-1");
        Assert.Equal("Accepted", night.Kind);
        await CompleteNightAsync(storyteller, "n1");

        // 白天 1：2、3、5 号投票把 3 号处决（当天上限用掉；5 席需要 3 票才过半）。
        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-mutant-day-1");
        Assert.Equal("Accepted", day.Kind);
        Assert.Equal("Accepted", (await two.InvokeAsync<CommandResultDto>("Nominate", 3, "test-mutant-nominate")).Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-mutant-sweep-1:start");
        Assert.Equal("Accepted", (await two.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-mutant-vote-2")).Kind);
        Assert.Equal("Accepted", (await three.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-mutant-vote-3")).Kind);
        Assert.Equal("Accepted", (await five.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-mutant-vote-5")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 5, "test-mutant-sweep-1");
        Assert.Equal("Accepted", (await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-mutant-count")).Kind);
        Assert.Equal("Accepted", (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-mutant-close")).Kind);

        var dayOne = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { Status: "Closed", Executed: 3 },
            Wait);
        Assert.NotNull(dayOne);

        // 夜晚 2：处罚 1 号（畸形秀演员）——当晚可处决，夜晚继续。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-mutant-night-2");
        Assert.Equal("Accepted", nightTwo.Kind);

        var punished = await storyteller.InvokeAsync<CommandResultDto>(
            "PunishExecution",
            1,
            "Mutant",
            "测试：1 号疯狂地证明自己是外来者",
            "test-mutant-punish");
        Assert.Equal("Accepted", punished.Kind);

        var afterPunish = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(entry => entry.Seat == 1
                && entry.Facts.Any(fact => fact.Dimension == "Life" && fact.Value == "Dead")),
            Wait);
        Assert.NotNull(afterPunish);

        // 当晚的处罚不占任何白天的上限：白天 1 的账不变，夜晚也照常走完。
        Assert.Equal(3, afterPunish!.Day!.Executed);
        Assert.False(afterPunish.PlanCompleted);

        // 行 1 前半（R-0022）：夜晚的死亡还没进公开面——牌面仍是白天 1 的公开状态，
        // 本日公告里没有 1 号；未公告的死亡对任何玩家（含本人）都不可见。
        var stillNight = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(stillNight);
        Assert.DoesNotContain(stillNight!.Lives, entry => entry.Seat.Value == 1 && entry.State == LifeState.Dead);
        Assert.DoesNotContain(stillNight.Announcements, entry => entry.Seat.Value == 1);

        await CompleteNightAsync(storyteller, "n2");

        // 白天 2：上限仍然可用——4 号提名 2 号并投票，正常处决。
        var dayTwo = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-mutant-day-2");
        Assert.Equal("Accepted", dayTwo.Kind);

        // 行 1 后半（R-0022）：开白天（黎明）后夜晚的死亡进入公开面——1 号死亡公告 + 牌面翻死亡；
        // 白天 1 的公告被新一天的批次替换（公告是"本日"的），且夜晚处罚的**理由**不进公开面。
        var afterDawn = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(afterDawn);
        Assert.Contains(afterDawn!.Lives, entry => entry.Seat.Value == 1 && entry.State == LifeState.Dead);
        Assert.Contains(afterDawn.Announcements, entry => entry.Seat.Value == 1 && entry.State == LifeState.Dead);
        Assert.DoesNotContain(afterDawn.Announcements, entry => entry.Seat.Value == 3);

        // 行 3：被处罚者自己的界面看得到死亡，权限位随之更新（不能发起提名）。
        var punishedDay = host.Session.GetPlayerView(new SeatId(1)).Day;
        Assert.NotNull(punishedDay);
        Assert.Contains(punishedDay!.Lives, entry => entry.Seat.Value == 1 && entry.State == LifeState.Dead);
        Assert.False(punishedDay.CanNominate);

        Assert.Equal("Accepted", (await four.InvokeAsync<CommandResultDto>("Nominate", 2, "test-mutant-nominate-2")).Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-mutant-sweep-2:start");
        Assert.Equal("Accepted", (await four.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-mutant-vote-4")).Kind);
        Assert.Equal("Accepted", (await five.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-mutant-vote-5-2")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 5, "test-mutant-sweep-2");
        Assert.Equal("Accepted", (await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-mutant-count-2")).Kind);
        Assert.Equal("Accepted", (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-mutant-close-2")).Kind);

        var dayTwoView = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { DayNumber: 2, Status: "Closed", Executed: 2 },
            Wait);
        Assert.NotNull(dayTwoView);

        // 事件流：夜晚处罚的 DayNumber = null（不写任何白天的账）。
        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        Assert.Single(stored, item => item.Event is ExecutedEvent
        {
            Kind: ExecutionKind.MutantMadness,
            DayNumber: null,
            Seat.Value: 1,
        });
        Assert.Single(stored, item => item.Event is ExecutedEvent
        {
            Kind: ExecutionKind.Day,
            DayNumber: 1,
            Seat.Value: 3,
        });
    }

    /// <summary>白天进行中重启宿主：疯狂要求随快照与事件流恢复，重启后处罚仍然成立。</summary>
    [Fact]
    public async Task RestartDuringTheMadnessDay_KeepsTheRequirementAndThePunishment()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-test-madness-restart-{Guid.NewGuid():N}.db");
        await using (var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false,
            autoStartTestNight: false))
        {
            await using var storyteller = await host.ConnectStorytellerAsync();
            await OpenCerenovusDayAsync(host, storyteller);
        }

        await using (var restarted = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true,
            autoStartTestNight: false))
        {
            await using var storyteller2 = await restarted.ConnectStorytellerAsync();
            var restored = await storyteller2.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.Equal("Day", restored.Phase);
            Assert.Contains("钟表匠", restored.Seats.Single(entry => entry.Seat == 4).Madnesses);

            var punished = await storyteller2.InvokeAsync<CommandResultDto>(
                "PunishExecution",
                4,
                "Cerenovus",
                "测试：重启后处罚",
                "test-madness-restart-punish");
            Assert.Equal("Accepted", punished.Kind);

            var after = await TestServerHost.WaitForViewAsync(
                storyteller2,
                candidate => candidate.Seats.Any(entry => entry.Seat == 4
                    && entry.Facts.Any(fact => fact.Dimension == "Life" && fact.Value == "Dead")),
                Wait);
            Assert.NotNull(after);
        }
    }

    /// <summary>
    /// 分配 → 首夜 → 洗脑师两维选择 4 号证明「钟表匠」→ 走完首夜 → 开白天；
    /// 返回 4 号玩家客户端（他收到了私密告知）。
    /// </summary>
    private static async Task<GameClient> OpenCerenovusDayAsync(TestServerHost host, GameClient storyteller)
    {
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            CerenovusAssignments(),
            "test-madness-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var cerenovus = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        var target = await host.ConnectSeatAsync(new SeatId(4));

        var started = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-madness-night-1");
        Assert.Equal("Accepted", started.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait), "洗脑师没有收到操作请求");

        var request = requests.First();
        Assert.Equal(1, request.Seat);
        Assert.Contains("洗脑师", request.Context, StringComparison.Ordinal);
        Assert.Equal(5, request.Options.Length);
        Assert.Equal(17, request.SecondaryOptions.Length);

        var answered = await cerenovus.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            request.RequestId,
            "seat:4|clockmaker",
            "test-madness-answer",
            1L);
        Assert.Equal("Accepted", answered.Kind);

        // 要求进账（说书人魔典标记）+ 目标收到私密告知。
        var withRequirement = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(entry => entry.Seat == 4
                && entry.Madnesses.Contains("钟表匠", StringComparer.Ordinal)),
            Wait);
        Assert.NotNull(withRequirement);

        var targetInformation = host.Session.GetPlayerView(new SeatId(4)).InformationResults;
        Assert.Contains(targetInformation, information =>
            information.Content.Contains("洗脑师", StringComparison.Ordinal)
            && information.Content.Contains("钟表匠", StringComparison.Ordinal));

        await CompleteNightAsync(storyteller, "n1");

        var dayStarted = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-madness-day-1");
        Assert.Equal("Accepted", dayStarted.Kind);
        return target;
    }

    /// <summary>
    /// 五席：1 号洗脑师 + 钟表匠 / 诺-达鲺 / 筑梦师 / 呆瓜。
    /// 被处罚目标固定是 4 号（筑梦师）——**不能让目标是唯一的恶魔**：处罚处决会当场触发
    /// 「所有恶魔均死亡 → 善良获胜」（规则正确行为）；五席也让"处罚 → 第二夜 → 第二天"
    /// 这条链路不会撞上「仅剩两名存活 → 邪恶获胜」。
    /// </summary>
    private static SeatCharacterAssignmentDto[] CerenovusAssignments() =>
    [
        new() { Seat = 1, Character = "cerenovus" },
        new() { Seat = 2, Character = "clockmaker" },
        new() { Seat = 3, Character = "no-dashii" },
        new() { Seat = 4, Character = "dreamer" },
        new() { Seat = 5, Character = "klutz" },
    ];

    /// <summary>
    /// 五席：1 号畸形秀演员 + 钟表匠 / 筑梦师 / 诺-达鲺 / 呆瓜。
    /// 两处死亡（白天 1 处决 + 夜晚处罚）之后仍剩 3 人存活，才够走到"第二天仍可处决"。
    /// </summary>
    private static SeatCharacterAssignmentDto[] MutantAssignments() =>
    [
        new() { Seat = 1, Character = "mutant" },
        new() { Seat = 2, Character = "clockmaker" },
        new() { Seat = 3, Character = "dreamer" },
        new() { Seat = 4, Character = "no-dashii" },
        new() { Seat = 5, Character = "klutz" },
    ];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本用例只真正结算与角色有关的那一步。</summary>
    /// <remarks>幂等键必须按"第几夜"区分：同一局内复用同一个键会命中回执重放（Duplicate），不是推进。</remarks>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
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
                "测试：越过无关槽位",
                $"test-madness-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                // 计划在「查视图」与「强推」之间被自动推进走完（0.05s 配额档下的固有竞态）：
                // 目标已经达成，不算失败。
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 24 次强推内没有走完");
    }
}
