using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 旅行者离场申请（D-0037）：**玩家发起 → 说书人裁定**，申请与裁定都进事件流、可回放。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《旅行者》· 2026-10-04 抓取（加入 / 离开流程）与 `rulings.md` R-0044 第 6 条：
/// 离开仍是说书人主持的动作，本批只把**发起人**从说书人改成旅行者本人——不是自助离开。
/// 说书人**始终保留直接移出**的权限（<c>RemoveTraveller</c>），那条路径顺手把待批申请结清。
/// </para>
/// <para>
/// 夹具形状：5 席局，1–4 席是非旅行者，第 5 席留给旅行者（<c>JoinTraveller</c> 落在该席）。
/// 跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </para>
/// </remarks>
public sealed class TravellerDepartureHostTests
{
    private static readonly SeatId TravellerSeat = new(5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 本人视图的**最新一份**（连接时拿一次快照，此后由推送更新）。
    /// </summary>
    /// <remarks>
    /// 入座时那一份重连包会随命令过期，所以断言一律读它——与真实玩家端同一条路：
    /// 快照 + 推送（D-0010 的同步口径）。
    /// </remarks>
    private sealed class PlayerFeed
    {
        public PlayerViewDto? Latest { get; set; }

        public bool Has(Func<PlayerViewDto, bool> predicate) => Latest is { } view && predicate(view);
    }

    /// <summary>申请 → 说书人视图与本人视图都能看到等待态；重复申请被拒。</summary>
    [Fact]
    public async Task Request_ShowsUpForStorytellerAndRequester_AndIsNotRepeatable()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, feed) = await SetUpTravellerAsync(host);

        var requested = await traveller.InvokeAsync<CommandResultDto>(
            "RequestTravellerDeparture",
            "我明天一早要出门",
            "departure-request-1");
        Assert.Equal("Accepted", requested.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var pending = Assert.Single(view.DepartureRequests);
        Assert.Equal(TravellerSeat.Value, pending.Seat);
        Assert.Equal("我明天一早要出门", pending.Note);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => feed.Has(item => item.PendingDepartureNote is not null), Wait),
            "本人视图没有更新到等待态");
        Assert.True(feed.Has(item => !item.CanRequestDeparture));

        // 重复申请：显式拒绝（同一席位同时只能有一条待批申请）。
        var again = await traveller.InvokeAsync<CommandResultDto>(
            "RequestTravellerDeparture",
            null,
            "departure-request-2");
        Assert.Equal("legality.departure_already_requested", again.RejectionCode);
    }

    /// <summary>反方向：**非旅行者**申请被拒（离场流程只适用于旅行者，D-0022 范围）。</summary>
    [Fact]
    public async Task Request_ByNonTraveller_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await SetUpTravellerAsync(host);
        await using var townsfolk = await host.ConnectSeatAsync(new SeatId(1));

        var rejected = await townsfolk.InvokeAsync<CommandResultDto>(
            "RequestTravellerDeparture",
            null,
            "departure-non-traveller");

        Assert.Equal("legality.not_a_traveller", rejected.RejectionCode);
    }

    /// <summary>说书人**驳回**：不离场、申请结清、本人看得到结论。</summary>
    [Fact]
    public async Task Reject_KeepsTheSeatAndRecordsTheRuling()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, feed) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, "再撑一会儿");

        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveTravellerDeparture",
            TravellerSeat.Value,
            false,
            "这一局还差你一个",
            "departure-reject-1");
        Assert.Equal("Accepted", resolved.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Empty(view.DepartureRequests);
        Assert.Contains(view.Seats, entry => entry.Seat == TravellerSeat.Value);

        // 本人视图：裁决结论与"可以再申请"都在（这份结论随快照下发，刷新不会丢）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => feed.Has(item => item.LastDepartureRuling is not null),
                Wait),
            "本人视图没有收到驳回结论");
        Assert.True(feed.Has(item => item.LastDepartureRuling!.Approved == false));
        Assert.True(feed.Has(item => item.LastDepartureRuling!.Note == "这一局还差你一个"));
        Assert.True(feed.Has(item => item.PendingDepartureNote is null));
        Assert.True(feed.Has(item => item.CanRequestDeparture));
        Assert.True(feed.Has(item => !item.Departed));
    }

    /// <summary>说书人**批准**：座位真的离场，事件流里申请与裁定各一条，本人看到"已离场"。</summary>
    [Fact]
    public async Task Approve_DepartsTheSeat_AndRecordsBothEvents()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, feed) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, "家里有事");

        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveTravellerDeparture",
            TravellerSeat.Value,
            true,
            "路上小心",
            "departure-approve-1");
        Assert.Equal("Accepted", resolved.Kind);

        // 席位账移除（R-0044 第 6 条：角色与生命标记一并移除）；离场账里有它。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.DoesNotContain(view.Seats, entry => entry.Seat == TravellerSeat.Value);
        Assert.Empty(view.DepartureRequests);
        Assert.True(host.Session.HasDeparted(TravellerSeat));

        // 三条事件按序落库：申请 → 裁定 → 离场（顺序有语义，折叠侧对乱序显式失败）。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var departureEvents = stored
            .Where(item => item.Event is TravellerDepartureRequestedEvent
                or TravellerDepartureResolvedEvent
                or TravellerDepartedEvent)
            .Select(item => item.Event.GetType().Name)
            .ToArray();
        Assert.Equal(
            ["TravellerDepartureRequestedEvent", "TravellerDepartureResolvedEvent", "TravellerDepartedEvent"],
            departureEvents);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => feed.Has(item => item.Departed), Wait),
            "本人视图没有更新到已离场");
        Assert.True(feed.Has(item => item.LastDepartureRuling!.Approved));
        Assert.True(feed.Has(item => !item.CanRequestDeparture));
    }

    /// <summary>反方向：**没有待批申请**时裁定被拒（不能凭空批一条不存在的申请）。</summary>
    [Fact]
    public async Task Resolve_WithoutPendingRequest_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, _, _) = await SetUpTravellerAsync(host);

        var rejected = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveTravellerDeparture",
            TravellerSeat.Value,
            true,
            null,
            "departure-resolve-without-request");

        Assert.Equal("legality.departure_not_requested", rejected.RejectionCode);
    }

    /// <summary>说书人**直接移出**（保留的权限）：座位离场，待批申请同时结清，不留悬空申请。</summary>
    [Fact]
    public async Task DirectRemoval_ClosesThePendingRequest()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, feed) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, "我先走了");

        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            TravellerSeat.Value,
            "说书人直接移出",
            "departure-direct-remove-1");
        Assert.Equal("Accepted", removed.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Empty(view.DepartureRequests);
        Assert.True(host.Session.HasDeparted(TravellerSeat));

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => feed.Has(item => item.Departed), Wait),
            "本人视图没有更新到已离场");
        Assert.True(feed.Has(item => item.LastDepartureRuling!.Approved));
    }

    /// <summary>
    /// 离场之后**原票仍能重连**（席位与票据保留，R-0044 第 6 条）：本人视图明说"已离场"。
    /// </summary>
    /// <remarks>
    /// 这条判据是本批**差点改坏**的那一条：把"离场席位不能再进"补成一条闸看着顺手，
    /// 但它与已登记的 R-0044 第 6 条（离场保留席位与票据、重连与复盘语义不动）相冲突。
    /// 现在改成"照进，但界面说实话"——离场事实由本人视图的 <c>Departed</c> 表达。
    /// </remarks>
    [Fact]
    public async Task DepartedSeat_StillAcceptsReconnect_AndSaysSoInTheOwnView()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, _, _) = await SetUpTravellerAsync(host);

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "RemoveTraveller",
                TravellerSeat.Value,
                "流程直接移出",
                "departure-reconnect-remove")).Kind);

        // 离场保留席位：说书人为这一席再签一枚邀请码，持码者仍进得来（本人视图会说"你已离场"）。
        var ticket = await host.IssueInviteCodeAsync(TravellerSeat);
        var account = await host.SeatFixtureAccountAsync(TravellerSeat);
        await using var raw = await host.ConnectAnonymousAsync();

        var rejoined = await raw.InvokeAsync<SeatJoinDto>("JoinByInviteCode", ticket, account.AccountSession, 0L);
        Assert.False(string.IsNullOrWhiteSpace(rejoined.Credential));
        Assert.True(rejoined.Bundle.View.Departed);
    }

    /// <summary>申请与裁定各成一个复盘步骤（D-0020：可回放；复盘目录对这两个事件类型有登记）。</summary>
    [Fact]
    public async Task DepartureRequest_IsReplayable()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, _) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, "复盘要看得到");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveTravellerDeparture",
                TravellerSeat.Value,
                false,
                "不许走",
                "departure-replay-reject")).Kind);

        var replay = await host.Game.Replay.ReadAsync(
            Actor.Storyteller(),
            afterSequence: 0,
            pageSize: 500,
            CancellationToken.None);
        var steps = replay.Steps.Select(step => $"{step.Summary} {step.Detail}").ToArray();
        Assert.Contains(steps, step => step.Contains("离场申请", StringComparison.Ordinal));
        Assert.Contains(steps, step => step.Contains("被驳回", StringComparison.Ordinal));
    }

    /// <summary>申请提交后**本人连接当场收到视图推送**：等待态不必等刷新（D-0010 的推送面）。</summary>
    [Fact]
    public async Task Request_PushesTheRequesterViewImmediately()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (_, traveller, feed) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, "推给我");

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => feed.Has(item => item.PendingDepartureNote == "推给我"),
                Wait),
            "本人视图推送没到（等待态只能等重连快照）");
    }

    /// <summary>
    /// **不写理由**的申请同样要看得见等待态（默认路径：理由是可选的）。
    /// </summary>
    /// <remarks>
    /// 界面装置（`tools/verify-table-access.mjs`）第一版咬出来的缺陷：本人视图只用
    /// "理由是不是 null"表示"有没有待批申请"，于是**不写理由**这条默认路径下，玩家点完「申请离场」
    /// 之后界面上整块离场区直接消失——申请确实到了说书人，本人却没有任何交代。
    /// 修法是把两件事拆开：<c>HasPendingDeparture</c> 回答"有没有"，理由是另一件事。
    /// </remarks>
    [Fact]
    public async Task Request_WithoutNote_StillShowsThePendingState()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        var (storyteller, traveller, feed) = await SetUpTravellerAsync(host);

        await RequestAsync(traveller, note: null);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => feed.Has(item => item.HasPendingDeparture), Wait),
            "不写理由的申请没有让本人看到等待态（整块离场区会消失）");
        Assert.True(feed.Has(item => item.PendingDepartureNote is null));

        // 说书人那一侧不受理由有无影响：待批行照样在。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Single(view.DepartureRequests);
    }

    /// <summary>夹具：5 席局，1–4 席非旅行者，第 5 席加入一名善良旅行者并让他本人入座。</summary>
    private static async Task<(GameClient Storyteller, GameClient Traveller, PlayerFeed Feed)> SetUpTravellerAsync(
        TestServerHost host)
    {
        var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "clockmaker" },
                new() { Seat = 2, Character = "artist" },
                new() { Seat = 3, Character = "klutz" },
                new() { Seat = 4, Character = "mutant" },
            },
            "departure-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            TravellerSeat.Value,
            "deviant",
            "Good",
            null,
            "departure-join-traveller");
        Assert.Equal("Accepted", joined.Kind);

        // 旅行者本人那一台设备：凭邀请码入座（他也是"必须登录"里的普通一员）。
        // 视图用快照打底、推送跟进——与真实玩家端同一条路。连接由宿主统一释放（`await using` 会在这里
        // 提前把它关掉，助手返回后那台"设备"就没了——实测踩过）。
        var feed = new PlayerFeed { Latest = host.Bundles.GetValueOrDefault(TravellerSeat)?.View };
        var traveller = await host.ConnectSeatAsync(
            TravellerSeat,
            onPlayerViewChanged: (_, view) => feed.Latest = view);
        feed.Latest ??= host.Bundles[TravellerSeat].View;
        return (storyteller, traveller, feed);
    }

    private static async Task RequestAsync(GameClient traveller, string? note)
    {
        var requested = await traveller.InvokeAsync<CommandResultDto>(
            "RequestTravellerDeparture",
            note,
            $"departure-request-{Guid.NewGuid():N}");
        Assert.Equal("Accepted", requested.Kind);
    }
}
