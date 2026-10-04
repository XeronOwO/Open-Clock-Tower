using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 屠夫窗口的真宿主链路（票据 `traveller-and-exile` · D4 / R-0050）：首次处决 → 白天保持 Open + 窗口事件
/// → 屠夫额外提名 → 只按半数落靶 → 第二次 CloseDay 执行并直接关账，不再开第二个窗口。
/// </summary>
/// <remarks>
/// 跑的是真宿主 + 真 SignalR + 真 SQLite；断言落在**事件流**与公开投影上——它们是重启 / 重连 / 复盘
/// 共同重建的事实来源（D-0010）。规则来源见百科《屠夫》· 2026-10-04 抓取 与 <c>docs/standard/rulings.md</c> R-0050。
/// </remarks>
public sealed class ButcherHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ButcherWindow_OpensAfterFirstExecution_ThenExtraNominationExecutesAndCloses()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithButcherAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));
        await using var seat4 = await host.ConnectSeatAsync(new SeatId(4));
        await using var butcher = await host.ConnectSeatAsync(session.ButcherSeat);

        // 第一轮常规提名：1 号提 2 号，3 票（6 席存活的一半）→ 达线。
        Assert.Equal(
            "Accepted",
            (await seat1.InvokeAsync<CommandResultDto>("Nominate", 2, "test-butcher-nominate-1")).Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-butcher-sweep-1");
        Assert.Equal(
            "Accepted",
            (await seat2.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-butcher-vote-1a")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat3.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-butcher-vote-1b")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat4.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-butcher-vote-1c")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, session.SeatCount, "test-butcher-sweep-1");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-butcher-count-1")).Kind);

        // 第一次 CloseDay：处决 2 号；屠夫（6 号）存活且能力生效 → 打开窗口，白天保持 Open。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-butcher-close-1")).Kind);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var gameEvents = events.Select(item => item.Event).ToArray();
        var window = Assert.Single(gameEvents.OfType<ExtraNominationWindowOpenedEvent>());
        Assert.Equal(session.ButcherSeat, window.Seat);
        Assert.DoesNotContain(gameEvents.OfType<DayClosedEvent>(), item => item.DayNumber == 1);
        Assert.Equal(DayStatus.Open, host.Session.GetPlayerView(new SeatId(1)).Day!.PublicView.Status);

        // 屠夫本人发起额外提名（目标是当天没被提名过的 1 号），走原收票链路。
        Assert.Equal(
            "Accepted",
            (await butcher.InvokeAsync<CommandResultDto>("NominateExtra", 1, "test-butcher-extra")).Kind);
        await VoteSweepTestDriver.StartAsync(host, 2, "test-butcher-sweep-2");
        Assert.Equal(
            "Accepted",
            (await seat1.InvokeAsync<CommandResultDto>("CastVote", 2, true, "test-butcher-vote-2a")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat3.InvokeAsync<CommandResultDto>("CastVote", 2, true, "test-butcher-vote-2b")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat4.InvokeAsync<CommandResultDto>("CastVote", 2, true, "test-butcher-vote-2c")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 2, session.SeatCount, "test-butcher-sweep-2");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 2, "test-butcher-count-2")).Kind);

        // 票数口径：额外提名的 3 票与第一次持平（没有更多），按《屠夫》仍应落靶（R-0050 第 4 条）。
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day?.AboutToBeExecuted == 1,
            Wait);
        Assert.Equal(1, view!.Day!.AboutToBeExecuted);

        // 第二次 CloseDay：处决 1 号并直接关账，不再开第二个窗口。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-butcher-close-2")).Kind);

        events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        gameEvents = events.Select(item => item.Event).ToArray();
        Assert.Equal(2, gameEvents.OfType<ExecutedEvent>().Count());
        Assert.Single(gameEvents.OfType<ExtraNominationMadeEvent>());
        Assert.Single(gameEvents.OfType<ExtraNominationWindowOpenedEvent>());
        Assert.Single(gameEvents.OfType<DayClosedEvent>());

        var deaths = gameEvents
            .OfType<SeatStateChangedEvent>()
            .Where(item => item.Life == LifeState.Dead)
            .ToArray();
        Assert.Contains(deaths, item => item.Seat == new SeatId(1));
        Assert.Contains(deaths, item => item.Seat == new SeatId(2));

        view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { Status: "Closed" },
            Wait);
        Assert.NotNull(view);
    }

    /// <summary>当天没有任何处决 → 不开窗、直接关账（《屠夫》：「如果当天没有发生任何处决，无法使用能力」）。</summary>
    [Fact]
    public async Task NoExecution_DoesNotOpenAWindow()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithButcherAsync(host);
        var storyteller = session.Storyteller;

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-butcher-no-exec-close")).Kind);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var gameEvents = events.Select(item => item.Event).ToArray();
        Assert.Single(gameEvents.OfType<DayClosedEvent>());
        Assert.Empty(gameEvents.OfType<ExtraNominationWindowOpenedEvent>());
        Assert.Empty(gameEvents.OfType<ExecutedEvent>());
    }

    /// <summary>5 席固定角色（覆盖四类型；白天契约都已实现，屠夫作为旅行者单独加入）。</summary>
    private static SeatCharacterAssignmentDto[] FiveAssignments() =>
    [
        new() { Seat = 1, Character = "clockmaker" },
        new() { Seat = 2, Character = "dreamer" },
        new() { Seat = 3, Character = "artist" },
        new() { Seat = 4, Character = "klutz" },
        new() { Seat = 5, Character = "no-dashii" },
    ];

    /// <summary>开一个带 1 名屠夫的白天：分配 5 席 → 追加旅行者席位 → 走完夹具夜晚 → 开白天。</summary>
    private static async Task<ButcherSession> SetUpDayWithButcherAsync(TestServerHost host)
    {
        var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FiveAssignments(),
            "test-butcher-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "butcher",
            "Good",
            null,
            "test-butcher-join");
        Assert.Equal("Accepted", joined.Kind);
        var butcherSeat = new SeatId(joined.IssuedSeat!.Value);

        await CompleteFixtureNightAsync(host, storyteller, butcherSeat.Value);
        var started = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-butcher-start-day");
        Assert.Equal("Accepted", started.Kind);
        return new ButcherSession(storyteller, butcherSeat, butcherSeat.Value);
    }

    /// <summary>用宿主身份开夹具夜晚，再逐槽强推到完成（把状态推进到"可以开白天"）。</summary>
    private static async Task CompleteFixtureNightAsync(TestServerHost host, GameClient storyteller, int seatCount)
    {
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
            "test-butcher-fixture-night",
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
                $"test-butcher-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夹具夜晚在 12 次强推内没有走完");
    }

    /// <summary>一次用例内持有的说书人客户端与屠夫席位（用例收尾时释放客户端）。</summary>
    private sealed record ButcherSession(GameClient Storyteller, SeatId ButcherSeat, int SeatCount) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Storyteller.DisposeAsync();
    }
}
