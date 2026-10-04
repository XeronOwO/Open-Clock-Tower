using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 流莺「夜访」的真宿主链路（票据 `traveller-and-exile` D5 首批）：旅行者加入 → 其他夜晚的黄昏槽 →
/// 目标选择 → 说书人「同意 / 拒绝 / 同死」裁定 → 私密信息与夜死 → 黎明公告；
/// 跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《流莺》· 2026-10-04 抓取（角色能力 / 角色简介 / 运作方式 / 提示标记）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0051，夜死公告见 R-0022。
/// 场景（5 席 + 加入第 6 席）：1 方古（恶魔，次夜击杀对照）、2 呆瓜、3 畸形秀演员、4 贤者、5 艺术家，
/// 第 6 席流莺（旅行者，走加入流程）。五席首夜都没有行动格，首夜自然走完；白天的相关角色
/// （呆瓜 / 畸形秀演员 / 艺术家）都已在 DayActions 登记覆盖；不处决也不会触发涡流式的胜负
/// （本场景没有涡流）。
/// </remarks>
public sealed class HarlotHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 同意且同死：信息只到流莺本人；流莺与被选中玩家在当夜死亡、黎明公告；
    /// 无关席位的公开面在此之前保持「存活」（夜死累积到黎明，R-0022）。
    /// </summary>
    [Fact]
    public async Task HarlotVisit_AgreeAndDie_PublishesBothDeathsAtDawn()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "klutz"), (3, "mutant"), (4, "sage"), (5, "artist")),
            "test-harlot-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 旅行者走加入流程（开局分配禁止旅行者）：追加席位 6 并签发新票据。
        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Good",
            null,
            "test-harlot-join");
        Assert.True(
            joined.Kind == "Accepted",
            $"加入流莺被拒：{joined.RejectionCode} {joined.RejectionMessage}");
        Assert.Equal(6, joined.IssuedSeat);

        OperationRequestDto? harlotRequest = null;
        InformationResultDto? harlotInfo = null;
        await using var harlot = await host.ConnectSeatAsync(
            new SeatId(6),
            asked => harlotRequest = asked,
            onInformation: info => harlotInfo = info);
        await using var demon = await host.ConnectSeatAsync(new SeatId(1));

        // 首夜：流莺不行动（顺序表只在其他夜晚给她槽位）。
        var nightOne = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-harlot-night-1");
        Assert.Equal("Accepted", nightOne.Kind);
        var nightOneDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightOneDone!.PlanCompleted, "首夜没有自然走完（配额未推进到收口）");
        Assert.Null(host.Session.GetPlayerView(new SeatId(6)).PendingRequest);

        // 白天 1：不处决，开完即关。
        var dayOne = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-harlot-day-1");
        Assert.True(
            dayOne.Kind == "Accepted",
            $"StartDay 被拒：{dayOne.RejectionCode} {dayOne.RejectionMessage}");
        var closedOne = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-harlot-close-day-1");
        Assert.Equal("Accepted", closedOne.Kind);

        // 次夜：黄昏槽 → 流莺收到目标选择请求。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-harlot-night-2");
        Assert.True(
            nightTwo.Kind == "Accepted",
            $"StartNight(2) 没被接受：{nightTwo.RejectionCode} {nightTwo.RejectionMessage}");
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => harlotRequest is not null, Wait),
            "流莺没有收到夜访请求（黄昏槽没有开出来）");

        var targeted = await harlot.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            harlotRequest!.RequestId,
            "seat:3",
            "test-harlot-target",
            1L);
        Assert.Equal("Accepted", targeted.Kind);

        // 说书人裁定：提示按账推演真实角色；一条裁定收口「拒绝 / 同意 / 同死」。
        var decision = await WaitForSlotDecisionAsync(storyteller, "harlot");
        Assert.Contains("3 号", decision.AwaitingDecisionContext!, StringComparison.Ordinal);
        Assert.Contains("畸形秀演员", decision.AwaitingDecisionContext!, StringComparison.Ordinal);

        var ruled = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            "agree-kill",
            null,
            "test-harlot-rule");
        Assert.Equal("Accepted", ruled.Kind);

        // 信息只到流莺本人：只报角色、不报阵营。
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => harlotInfo is not null, Wait),
            "流莺没有收到角色信息");
        Assert.Contains("3 号玩家的角色是「畸形秀演员」", harlotInfo!.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("阵营", harlotInfo.Content, StringComparison.Ordinal);

        // 夜死落账：流莺与 3 号都死；公开面在黎明之前**不公告**（观众仍看到存活）。
        Assert.Equal(LifeState.Dead, LifeOf(host, 3));
        Assert.Equal(LifeState.Dead, LifeOf(host, 6));
        Assert.Equal(LifeState.Alive, PublicLifeOf(host, viewer: 2, seat: 3));
        Assert.Equal(LifeState.Alive, PublicLifeOf(host, viewer: 2, seat: 6));

        // 夜晚继续：恶魔格照常行动（对照：与流莺无关的击杀不受影响）。
        var kill = await WaitForRequestAsync(host, new SeatId(1));
        var killed = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            kill.Id.Value,
            "seat:5",
            "test-harlot-demon-kill",
            1L);
        Assert.Equal("Accepted", killed.Kind);

        var nightDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightDone!.PlanCompleted, "流莺结清后夜晚没有走完");

        // 黎明公告：本夜的三条死亡（流莺 / 3 号 / 5 号）进公开面。
        var dayTwo = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-harlot-day-2");
        Assert.Equal("Accepted", dayTwo.Kind);
        var dayView = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(dayView);
        Assert.Contains(dayView!.Announcements, entry => entry.Seat == new SeatId(3) && entry.State == LifeState.Dead);
        Assert.Contains(dayView.Announcements, entry => entry.Seat == new SeatId(6) && entry.State == LifeState.Dead);
        Assert.Contains(dayView.Announcements, entry => entry.Seat == new SeatId(5) && entry.State == LifeState.Dead);
        Assert.Equal(LifeState.Dead, PublicLifeOf(host, viewer: 2, seat: 6));
    }

    /// <summary>拒绝：无事发生——没有信息、没有死亡，夜晚照常收口。</summary>
    [Fact]
    public async Task HarlotVisit_Refused_LeavesNoInformationAndNoDeaths()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "klutz"), (3, "mutant"), (4, "sage"), (5, "artist")),
            "test-harlot-refuse-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Good",
            null,
            "test-harlot-refuse-join");
        Assert.Equal("Accepted", joined.Kind);

        OperationRequestDto? harlotRequest = null;
        await using var harlot = await host.ConnectSeatAsync(new SeatId(6), asked => harlotRequest = asked);
        await using var demon = await host.ConnectSeatAsync(new SeatId(1));

        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-harlot-refuse-night-1");
        Assert.Equal("Accepted", nightOne.Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted);

        var dayOne = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-harlot-refuse-day-1");
        Assert.True(
            dayOne.Kind == "Accepted",
            $"StartDay 被拒：{dayOne.RejectionCode} {dayOne.RejectionMessage}");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-harlot-refuse-close-day-1")).Kind);

        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-harlot-refuse-night-2");
        Assert.Equal("Accepted", nightTwo.Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => harlotRequest is not null, Wait),
            "流莺没有收到夜访请求");

        var targeted = await harlot.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            harlotRequest!.RequestId,
            "seat:2",
            "test-harlot-refuse-target",
            1L);
        Assert.Equal("Accepted", targeted.Kind);

        var decision = await WaitForSlotDecisionAsync(storyteller, "harlot");
        var ruled = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            "refuse",
            null,
            "test-harlot-refuse-rule");
        Assert.Equal("Accepted", ruled.Kind);

        // 夜继续走完（恶魔格照常）：拒绝本身不产生任何挂起。
        var kill = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await demon.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:5",
                "test-harlot-refuse-demon-kill",
                1L)).Kind);
        var nightDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightDone!.PlanCompleted, "拒绝后夜晚没有走完");

        // 零效果：流莺没有信息结果；2 号与 6 号都存活；公开面没有他们的死亡公告。
        var harlotView = host.Session.GetPlayerView(new SeatId(6));
        Assert.Empty(harlotView.InformationResults);
        Assert.Equal(LifeState.Alive, LifeOf(host, 2));
        Assert.Equal(LifeState.Alive, LifeOf(host, 6));

        var dayTwo = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-harlot-refuse-day-2");
        Assert.Equal("Accepted", dayTwo.Kind);
        var dayView = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(dayView);
        Assert.DoesNotContain(dayView!.Announcements, entry => entry.Seat == new SeatId(2));
        Assert.DoesNotContain(dayView.Announcements, entry => entry.Seat == new SeatId(6));
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

    /// <summary>某名玩家此刻看到的公开生死（R-0022 的公开面；还没有开过白天时为 null）。</summary>
    private static LifeState? PublicLifeOf(TestServerHost host, int viewer, int seat) =>
        host.Session.GetPlayerView(new SeatId(viewer)).Day?.Lives
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .State;

    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(seat).PendingRequest is not null,
                Wait),
            $"席位 {seat.Value} 没有收到操作请求");

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }

    private static async Task<StorytellerViewDto> WaitForSlotDecisionAsync(GameClient storyteller, string slotId)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null && candidate.CurrentSlotId == slotId,
            Wait);
        Assert.True(
            view is { AwaitingDecisionId: not null } && view.CurrentSlotId == slotId,
            $"没有等到槽位 {slotId} 上的裁定（最后视图 slot={view?.CurrentSlotId ?? "无"} "
                + $"挂起={view?.AwaitingDecisionId ?? "无"}）");
        return view!;
    }
}
