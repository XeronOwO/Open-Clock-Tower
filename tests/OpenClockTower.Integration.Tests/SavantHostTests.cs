using System.Collections.Concurrent;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 博学者的真宿主链路（R-0057）：带他的局开得了白天（不再 <c>legality.day_contract_missing</c>）→
/// 本人白天要两条信息 → 说书人裁定 → **两条信息只到本人** → 每个白天只能要一次 →
/// 结清后白天照常收口。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《博学者》· 2026-10-01 抓取（角色能力 / 角色简介）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0057。
/// </remarks>
public sealed class SavantHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static readonly SeatId Savant = new(1);

    /// <summary>
    /// 一局五席：1 号博学者、2 号艺术家、3 号呆瓜、4 号畸形秀演员、5 号方古（首夜不行动）。
    /// 首夜因此没有需要操作的槽位，白天里只有博学者一处。
    /// </summary>
    [Fact]
    public async Task Savant_AsksForTwoMessages_OncePerDay_OnlyToSelf()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "savant"), (2, "artist"), (3, "klutz"), (4, "mutant"), (5, "fang-gu")),
                "test-savant-assign")).Kind);

        var pushed = new ConcurrentQueue<PlayerViewDto>();
        await using var savant = await host.ConnectSeatAsync(
            Savant,
            onPlayerViewChanged: (_, view) => pushed.Enqueue(view));
        await using var _ = await host.ConnectSeatAsync(new SeatId(2));

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-savant-night-1")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");

        // 开白天：带博学者的局不再被 legality.day_contract_missing 拒绝；入口只发给本人。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-savant-day-1")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Savant).CanAskSavantQuestion,
                Wait),
            "博学者没有拿到「要两条信息」的入口");
        Assert.False(host.Session.GetPlayerView(new SeatId(2)).CanAskSavantQuestion);

        // 要两条信息：私密请求进事件流、开归属席位的裁定点（说书人视角能看到）。
        Assert.Equal(
            "Accepted",
            (await savant.InvokeAsync<CommandResultDto>("AskSavantQuestion", "test-savant-ask-1")).Kind);
        var decision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.AwaitingDecisionSeat == 1,
            Wait);
        Assert.NotNull(decision);

        // 未结清时不能再来一次（同一时刻最多一条）。
        var pendingAgain = await savant.InvokeAsync<CommandResultDto>("AskSavantQuestion", "test-savant-ask-2");
        Assert.Equal("Rejected", pendingAgain.Kind);
        Assert.Equal("savant.question_pending", pendingAgain.RejectionCode);

        // 说书人给两条（用 | 分隔）：两条各发一条信息结果，收件人只有博学者本人。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                decision!.AwaitingDecisionId,
                "3 号是镇民|4 号是爪牙",
                null,
                "test-savant-resolve")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Savant).InformationResults.Count == 2,
                Wait),
            "两条信息没有下发");
        var results = host.Session.GetPlayerView(Savant).InformationResults;
        Assert.Equal("3 号是镇民", results[0].Content);
        Assert.Equal("4 号是爪牙", results[1].Content);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(2)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(3)).InformationResults);

        // 每个白天一次：同一天再要一次被拒，入口也随之消失。
        var todayAgain = await savant.InvokeAsync<CommandResultDto>("AskSavantQuestion", "test-savant-ask-3");
        Assert.Equal("Rejected", todayAgain.Kind);
        Assert.Equal("savant.already_asked_today", todayAgain.RejectionCode);
        Assert.False(host.Session.GetPlayerView(Savant).CanAskSavantQuestion);

        // 结清之后白天照常收口（未结清时才挡关账）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-savant-close-day-1")).Kind);
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
