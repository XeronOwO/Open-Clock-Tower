using System.Collections.Concurrent;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 限次信息族在真实宿主里的整条链路（平台口径见 <c>docs/standard/rulings.md</c> R-0040）：
/// 女裁缝每局限一次、用后不再唤醒；艺术家白天主动提问、三种回答消耗 / 「要求重问」不消耗；
/// 以及哲学家摇头不算使用（同族记账修复的回归）。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；夹具只放会开请求的角色（女裁缝 / 艺术家 / 哲学家），
/// 其他席位用 0.05 秒配额自动推进或强推越过，避免无关交互干扰断言。
/// </remarks>
public sealed class SeamstressArtistHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>女裁缝：选两名玩家 → 说书人裁定 → 信息只到本人、失能标记进说书人视图 → 次夜不再唤醒。</summary>
    [Fact]
    public async Task Seamstress_UsesOnce_ThenNeverWakesAgain()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "seamstress"), (2, "klutz"), (3, "mutant"), (4, "sweetheart"), (5, "no-dashii")),
                "test-seamstress-assign")).Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-seamstress-night-1")).Kind);

        var request = await WaitForRequestAsync(host, new SeatId(1));
        var options = request.Prompt.Options.Select(option => option.Value).ToArray();
        Assert.Contains("pair:2+3", options);
        Assert.Contains("decline", options);

        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                request.Id.Value,
                "pair:2+3",
                "test-seamstress-answer",
                1L)).Kind);

        // 说书人裁定「是」。
        var decision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null,
            Wait);
        Assert.NotNull(decision);
        Assert.Equal(1, decision!.AwaitingDecisionSeat);
        Assert.Contains("2 号与 3 号", decision.AwaitingDecisionContext!, StringComparison.Ordinal);
        var yes = decision.AwaitingDecisionOptions!.Single(option => option.Value == "yes");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                decision.AwaitingDecisionId!,
                yes.Value,
                null,
                "test-seamstress-resolve")).Kind);

        // 信息只到本人（内容由平台按裁定选项合成），失能标记只说书人可见。
        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetPlayerView(new SeatId(1)).InformationResults
                .Any(information => information.Ability == new AbilityId("seamstress")),
            Wait));
        var information = host.Session.GetPlayerView(new SeatId(1)).InformationResults
            .Single(item => item.Ability == new AbilityId("seamstress"));
        Assert.Contains("2 号与 3 号", information.Content, StringComparison.Ordinal);
        Assert.Contains("属于同一阵营", information.Content, StringComparison.Ordinal);

        Assert.Contains(
            host.Session.GetStorytellerView().LostAbilityMarkers,
            marker => marker.Seat == new SeatId(1) && marker.Ability == new AbilityId("seamstress"));
        Assert.DoesNotContain(
            host.Session.GetPlayerView(new SeatId(2)).InformationResults,
            item => item.Ability == new AbilityId("seamstress"));

        // 第 1 夜 → 白天 → 第 2 夜。
        await CompleteNightAsync(storyteller, "seamstress-1");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-seamstress-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-seamstress-close-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-seamstress-night-2")).Kind);
        await CompleteNightAsync(storyteller, "seamstress-2");

        // 事件流证据：第 2 夜没有任何发给她的请求；她的格是可归因的跳过（不再唤醒）。
        var events = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None))
            .Select(item => item.Event)
            .ToArray();
        Assert.DoesNotContain(
            events.OfType<OperationRequestIssuedEvent>(),
            item => item.Request.Addressee == new SeatId(1)
                && item.Request.Origin.PlanLabel == "sv:night-2");
        Assert.Contains(
            events.OfType<PromptSkippedEvent>(),
            item => item.SlotId.Value == "seamstress"
                && item.Reason.Contains("不再被唤醒", StringComparison.Ordinal));
    }

    /// <summary>
    /// 艺术家：白天主动提问 → 说书人裁定；「要求重问」不消耗、可再问；三种回答消耗并落失能标记；
    /// 提问未结清时不能收口白天。
    /// </summary>
    [Fact]
    public async Task Artist_AnswerConsumes_ReturnDoesNot()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "artist"), (2, "klutz"), (3, "mutant"), (4, "sweetheart"), (5, "no-dashii")),
                "test-artist-assign")).Kind);

        var pushedViews = new ConcurrentQueue<PlayerViewDto>();
        await using var one = await host.ConnectSeatAsync(
            new SeatId(1),
            onPlayerViewChanged: (_, view) => pushedViews.Enqueue(view));
        // 第 1 夜：艺术家没有夜间行动，走完即可。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-artist-night-1")).Kind);
        await CompleteNightAsync(storyteller, "artist-1");

        // 白天：只有艺术家本人拿到提问入口（权限位由服务端下发），且入口在**在线连接**上即时出现——
        // 修复前权限位只随快照更新，白天开始后入口要等重连才出现（本票 E26 首跑实测的缺陷）。
        var beforeDayPush = pushedViews.Count;
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-artist-day-1")).Kind);
        var dayView = await WaitForPushedViewAsync(pushedViews, beforeDayPush, pushed => pushed.CanAskArtistQuestion);
        Assert.Equal(1, dayView.Seat);
        Assert.Equal("Day", dayView.Phase);
        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetPlayerView(new SeatId(1)).CanAskArtistQuestion,
            Wait));
        Assert.False(host.Session.GetPlayerView(new SeatId(2)).CanAskArtistQuestion);

        var beforeAskPush = pushedViews.Count;
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "AskArtistQuestion",
                "2 号是爪牙吗？",
                "test-artist-ask-1")).Kind);

        // 等待态（进行中问题）必须**在线**下发到本人：权限位关闭 + 带问题全文。
        var pendingView = await WaitForPushedViewAsync(
            pushedViews,
            beforeAskPush,
            pushed => pushed.PendingQuestion == "2 号是爪牙吗？" && !pushed.CanAskArtistQuestion);
        Assert.Equal(1, pendingView.Seat);

        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetStorytellerView().AwaitingDecision is not null,
            Wait));
        var view = host.Session.GetStorytellerView();
        Assert.Equal(new SeatId(1), view.AwaitingDecisionSeat);
        Assert.Contains("2 号是爪牙吗？", view.AwaitingDecision!.Prompt.Context, StringComparison.Ordinal);
        Assert.Equal("2 号是爪牙吗？", host.Session.GetPlayerView(new SeatId(1)).PendingQuestion);
        // 无关席位零下发：问题全文与已用尽能力都只对本人生效（D-0012）。
        Assert.Null(host.Session.GetPlayerView(new SeatId(2)).PendingQuestion);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(2)).ExhaustedAbilities);

        // 提问挂起：推进类命令被拒。
        var blocked = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-artist-close-blocked");
        Assert.Equal("Rejected", blocked.Kind);
        Assert.Equal("phase.artist_question_pending", blocked.RejectionCode);

        // 「要求重问」：不消耗、不落标记、可以再问（本人视图推送让等待态即时清掉、入口回来）。
        var beforeRetryPush = pushedViews.Count;
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                view.AwaitingDecision.Id.Value,
                "retry",
                null,
                "test-artist-retry")).Kind);
        await WaitForPushedViewAsync(
            pushedViews,
            beforeRetryPush,
            pushed => pushed.PendingQuestion is null && pushed.CanAskArtistQuestion);
        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetPlayerView(new SeatId(1)).PendingQuestion is null,
            Wait));
        Assert.True(host.Session.GetPlayerView(new SeatId(1)).CanAskArtistQuestion);
        Assert.DoesNotContain(
            host.Session.GetStorytellerView().LostAbilityMarkers,
            marker => marker.Seat == new SeatId(1));

        // 再问一次 + 真回答：消耗能力、下发信息、落失能标记。
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "AskArtistQuestion",
                "3 号是爪牙吗？",
                "test-artist-ask-2")).Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetStorytellerView().AwaitingDecision is not null,
            Wait));
        var second = host.Session.GetStorytellerView();
        var beforeAnswerPush = pushedViews.Count;
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                second.AwaitingDecision!.Id.Value,
                "no",
                null,
                "test-artist-answer")).Kind);
        var answeredView = await WaitForPushedViewAsync(
            pushedViews,
            beforeAnswerPush,
            pushed => !pushed.CanAskArtistQuestion && pushed.ExhaustedAbilities.Contains("artist"));
        Assert.Null(answeredView.PendingQuestion);

        Assert.True(await TestServerHost.WaitUntilAsync(
            () => host.Session.GetPlayerView(new SeatId(1)).ExhaustedAbilities.Contains("artist"),
            Wait));
        var player = host.Session.GetPlayerView(new SeatId(1));
        Assert.False(player.CanAskArtistQuestion);
        Assert.Null(player.PendingQuestion);
        var answer = player.InformationResults.Single(item => item.Ability == new AbilityId("artist"));
        Assert.Equal("不是", answer.Content);
        Assert.Contains(
            host.Session.GetStorytellerView().LostAbilityMarkers,
            marker => marker.Seat == new SeatId(1) && marker.Ability == new AbilityId("artist"));

        // 已用过：不能再问（每局限一次）。
        var again = await one.InvokeAsync<CommandResultDto>("AskArtistQuestion", "还能问吗？", "test-artist-ask-3");
        Assert.Equal("Rejected", again.Kind);
        Assert.Equal("artist.already_used", again.RejectionCode);

        // 结清后白天正常收口。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-artist-close-1")).Kind);
    }

    /// <summary>
    /// 哲学家摇头（不使用能力）**不算**用掉「每局限一次」：第 2 夜他仍然收到选择请求；
    /// 事件流里没有 philosopher.grant 的使用记录（同族记账修复的回归）。
    /// </summary>
    [Fact]
    public async Task PhilosopherDecline_DoesNotConsumeOncePerGame()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "philosopher"), (2, "klutz"), (3, "mutant"), (4, "sweetheart"), (5, "no-dashii")),
                "test-philosopher-decline-assign")).Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-philosopher-decline-night-1")).Kind);
        var first = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Contains("decline", first.Prompt.Options.Select(option => option.Value));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                first.Id.Value,
                "decline",
                "test-philosopher-decline",
                1L)).Kind);

        await CompleteNightAsync(storyteller, "philosopher-decline-1");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-philosopher-decline-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-philosopher-decline-close-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-philosopher-decline-night-2")).Kind);

        // 第 2 夜：摇头不算用掉——他仍然收到「获得能力」的选择。
        var second = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Contains("decline", second.Prompt.Options.Select(option => option.Value));

        // 事件流证据：没有任何 philosopher.grant 的使用记录。
        var events = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None))
            .Select(item => item.Event)
            .ToArray();
        Assert.DoesNotContain(
            events.OfType<AbilityResolvedEvent>(),
            item => item.Actor == new SeatId(1) && item.Ability == new AbilityId("philosopher.grant"));
    }

    /// <summary>
    /// 艺术家提问的两道闸的针对性运行证据（矩阵行 9）：不是白天 → 阶段闸 `phase.not_open_day` 拒绝；
    /// 白天里非艺术家席位 → 内核 `artist.not_artist` 拒绝；同日艺术家本人提问受理（正对照）。
    /// </summary>
    [Fact]
    public async Task AskArtistQuestion_RejectsOutsideDay_AndForNonArtist()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "artist"), (2, "klutz"), (3, "mutant"), (4, "sweetheart"), (5, "no-dashii")),
                "test-artist-gate-assign")).Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var two = await host.ConnectSeatAsync(new SeatId(2));

        // 不是白天（还没有开过任何阶段）：阶段闸拒绝，不产生事件。
        var tooEarly = await one.InvokeAsync<CommandResultDto>("AskArtistQuestion", "现在能问吗？", "test-artist-gate-early");
        Assert.Equal("Rejected", tooEarly.Kind);
        Assert.Equal("phase.not_open_day", tooEarly.RejectionCode);

        // 开夜 → 走完 → 开白天：白天里非艺术家席位提问被内核身份判定拒绝。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-artist-gate-night")).Kind);
        await CompleteNightAsync(storyteller, "artist-gate-1");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-artist-gate-day")).Kind);

        var notArtist = await two.InvokeAsync<CommandResultDto>(
            "AskArtistQuestion",
            "我不是艺术家？",
            "test-artist-gate-not-artist");
        Assert.Equal("Rejected", notArtist.Kind);
        Assert.Equal("artist.not_artist", notArtist.RejectionCode);

        // 正对照：同一次白天里，艺术家本人提问被受理（上面两条不是"白天没开"造成的假拒绝）。
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "AskArtistQuestion",
                "1 号是爪牙吗？",
                "test-artist-gate-control")).Kind);
    }

    /// <summary>等一条满足条件的本人视图推送（自 fromIndex 起；推送按连接有序，断言只看这一窗之后的）。</summary>
    private static async Task<PlayerViewDto> WaitForPushedViewAsync(
        ConcurrentQueue<PlayerViewDto> views,
        int fromIndex,
        Func<PlayerViewDto, bool> predicate)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => views.Skip(fromIndex).Any(predicate),
                Wait),
            $"没有等到期望的本人视图推送（自第 {fromIndex} 条起共 {views.Count} 条）");
        return views.Skip(fromIndex).First(predicate);
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算关心的几步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 64 次强推内没有走完");
    }

    /// <summary>等到某席位收到操作请求；没有则失败（不用 sleep 猜时序）。</summary>
    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(seat).PendingRequest is not null,
                Wait),
            $"席位 {seat.Value} 没有收到操作请求");

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }
}
