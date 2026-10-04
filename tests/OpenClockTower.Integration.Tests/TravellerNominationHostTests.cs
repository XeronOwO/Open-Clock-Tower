using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 旅行者与处决路径接缝的真宿主链路（票据 `traveller-and-exile` · R-0049）：旅行者可被提名、票数照记，
/// 但永远不进入「即将被处决」；白天关账不产生处决 / 死亡。
/// </summary>
/// <remarks>
/// 跑的是真宿主 + 真 SignalR + 真 SQLite；规则来源见印刷规则书提取文本 · 2026-10-04 抓取 · Travelers / 词汇表
/// 与钟楼百科《旅行者》· 2026-10-04 抓取，收口口径见 <c>docs/standard/rulings.md</c> R-0049。
/// </remarks>
public sealed class TravellerNominationHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task NominationOnATraveller_CountsButNeverLands_AndTheDayClosesWithoutDeath()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var session = await SetUpDayWithTravellerAsync(host);
        var storyteller = session.Storyteller;

        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));
        await using var seat4 = await host.ConnectSeatAsync(new SeatId(4));

        // 1 号提名旅行者（6 号）：提名照常受理（R-0049 第 1 条）。
        var nominated = await seat1.InvokeAsync<CommandResultDto>(
            "Nominate",
            session.TravellerSeat.Value,
            "test-traveller-nominate");
        Assert.Equal("Accepted", nominated.Kind);

        // 3 票（6 席存活的一半）——足以让普通角色落靶。
        await VoteSweepTestDriver.StartAsync(host, 1, "test-traveller-sweep");
        Assert.Equal(
            "Accepted",
            (await seat2.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-traveller-vote-a")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat3.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-traveller-vote-b")).Kind);
        Assert.Equal(
            "Accepted",
            (await seat4.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-traveller-vote-c")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, session.SeatCount, "test-traveller-sweep");

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-traveller-count");
        Assert.Equal("Accepted", counted.Kind);

        // 票数照记、进公开面，但旅行者永不进入「即将被处决」（R-0049 第 2 条）。
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Day is { } day
                && day.Nominations.Length > 0
                && day.Nominations[0].Status == "Counted",
            Wait);
        Assert.NotNull(view);
        Assert.Null(view!.Day!.AboutToBeExecuted);
        Assert.Equal(3, view.Day.Nominations[0].Votes);

        // 关账：没有处决事实、目标不死亡，白天正常结束。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-traveller-close")).Kind);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var gameEvents = events.Select(item => item.Event).ToArray();
        Assert.Empty(gameEvents.OfType<ExecutedEvent>());
        Assert.DoesNotContain(
            gameEvents.OfType<SeatStateChangedEvent>(),
            item => item.Seat == session.TravellerSeat && item.Life == LifeState.Dead);

        var publicLife = host.Session.GetPlayerView(new SeatId(1)).Day!.Lives;
        Assert.DoesNotContain(
            publicLife,
            item => item.Seat == session.TravellerSeat && item.State == LifeState.Dead);
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

    /// <summary>开一个带 1 名旅行者（怪咖）的白天：分配 5 席 → 追加旅行者席位 → 走完夹具夜晚 → 开白天。</summary>
    private static async Task<TravellerSession> SetUpDayWithTravellerAsync(TestServerHost host)
    {
        var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FiveAssignments(),
            "test-traveller-nomination-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-traveller-nomination-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = new SeatId(joined.IssuedSeat!.Value);

        await CompleteFixtureNightAsync(host, storyteller, travellerSeat.Value);
        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartDay",
            "test-traveller-nomination-start-day");
        Assert.Equal("Accepted", started.Kind);
        return new TravellerSession(storyteller, travellerSeat, travellerSeat.Value);
    }

    /// <summary>用宿主身份开夹具夜晚，再逐槽强推到完成（把状态推进到"可以开白天"）。</summary>
    private static async Task CompleteFixtureNightAsync(TestServerHost host, GameClient storyteller, int seatCount)
    {
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount) },
            "test-traveller-nomination-fixture-night",
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
                $"test-traveller-nomination-force-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夹具夜晚在 12 次强推内没有走完");
    }

    /// <summary>一次用例内持有的说书人客户端与旅行者席位（用例收尾时释放客户端）。</summary>
    private sealed record TravellerSession(GameClient Storyteller, SeatId TravellerSeat, int SeatCount)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Storyteller.DisposeAsync();
    }
}
