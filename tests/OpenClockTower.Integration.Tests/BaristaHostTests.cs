using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 咖啡师「二选一」的真宿主链路（票据 `traveller-and-exile` D5 第二批）：旅行者加入 → 黄昏槽 →
/// 说书人裁定（目标 + 效果）→「行动两次」的目标当夜二次结算 → 下个黄昏窗口收口；
/// 以及「清醒且健康」对常驻中毒的挂起与恢复。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《咖啡师》· 2026-10-04 抓取（角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0047 与 R-0052。
/// </remarks>
public sealed class BaristaHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 「行动两次」：筑梦师在同一个夜晚收到两次行动请求（标识带遍次），两条信息各自下发；
    /// 下个黄昏窗口收口（标记移除），不再是挂着的事实。
    /// </summary>
    [Fact]
    public async Task BaristaTwice_TargetActsTwice_AndWindowClosesNextDusk()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "dreamer"), (3, "klutz"), (4, "mutant"), (5, "sage")),
            "test-barista-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "barista",
            "Good",
            null,
            "test-barista-join");
        Assert.True(
            joined.Kind == "Accepted",
            $"加入咖啡师被拒：{joined.RejectionCode} {joined.RejectionMessage}");
        Assert.Equal(6, joined.IssuedSeat);

        OperationRequestDto? dreamerRequest = null;
        await using var dreamer = await host.ConnectSeatAsync(new SeatId(2), asked => dreamerRequest = asked);

        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barista-night-1");
        Assert.Equal("Accepted", nightOne.Kind);

        // 黄昏咖啡师槽：说书人二选一（这里是「行动两次」→ 2 号筑梦师）。
        var decision = await WaitForSlotDecisionAsync(storyteller, "barista");
        Assert.Contains("咖啡师", decision.AwaitingDecisionContext!, StringComparison.Ordinal);
        Assert.Contains(
            decision.AwaitingDecisionOptions ?? [],
            option => option.Value == "twice:seat:2");

        var ruled = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            "twice:seat:2",
            null,
            "test-barista-rule");
        Assert.Equal("Accepted", ruled.Kind);

        // 受影响玩家得知是哪个效果（信息只到本人）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(2)).InformationResults
                    .Any(result => result.Ability.Value == "barista"),
                Wait),
            "筑梦师没有收到「行动两次」的告知");

        // 第一次行动：请求标识不带遍次。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => dreamerRequest is not null && dreamerRequest.RequestId == "sv:night-1:dreamer",
                Wait),
            $"筑梦师没有收到第一次行动请求（最后：{dreamerRequest?.RequestId ?? "无"}）");
        Assert.Equal(
            "Accepted",
            (await dreamer.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                dreamerRequest!.RequestId,
                "seat:3",
                "test-barista-dreamer-1",
                1L)).Kind);

        // 筑梦师的信息由说书人裁定（选一枚错误项）→ 第一条信息。
        var firstDecision = await WaitForSlotDecisionAsync(storyteller, "dreamer");
        var firstOption = firstDecision.AwaitingDecisionOptions![0];
        Assert.False(string.IsNullOrEmpty(firstOption.Value));
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                firstDecision.AwaitingDecisionId,
                firstOption.Value,
                null,
                "test-barista-dreamer-rule-1")).Kind);

        // 第二次行动：同一槽位重进，请求标识带遍次。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => dreamerRequest is not null && dreamerRequest.RequestId == "sv:night-1:dreamer#2",
                Wait),
            $"筑梦师没有收到第二次行动请求（最后：{dreamerRequest?.RequestId ?? "无"}）");
        Assert.Equal(
            "Accepted",
            (await dreamer.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                dreamerRequest!.RequestId,
                "seat:3",
                "test-barista-dreamer-2",
                1L)).Kind);

        var secondDecision = await WaitForSlotDecisionAsync(storyteller, "dreamer");
        Assert.Equal("sv:night-1:dreamer#2:decision", secondDecision.AwaitingDecisionId);
        var secondOption = secondDecision.AwaitingDecisionOptions![0];
        Assert.False(string.IsNullOrEmpty(secondOption.Value));
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                secondDecision.AwaitingDecisionId,
                secondOption.Value,
                null,
                "test-barista-dreamer-rule-2")).Kind);

        var nightDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightDone!.PlanCompleted, "夜晚没有自然走完");

        var dreamerView = host.Session.GetPlayerView(new SeatId(2));
        Assert.Equal(2, dreamerView.InformationResults.Count(result => result.Ability.Value == "dreamer"));

        // 白天 1：开完即关（不处决）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-barista-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-barista-close-day-1")).Kind);

        // 下个黄昏：窗口收口（在触发下一晚的效果之前移除标记，R-0052 第 4 条）。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-barista-night-2");
        Assert.True(
            nightTwo.Kind == "Accepted",
            $"StartNight(2) 没被接受：{nightTwo.RejectionCode} {nightTwo.RejectionMessage}");

        var window = Assert.Single(
            host.Session.GetStorytellerView().PersistentEffects,
            effect => effect.Ability.Value == "barista");
        Assert.True(window.IsTerminated, "咖啡师窗口没有在下个黄昏收口");
    }

    /// <summary>
    /// 「清醒且健康」：诺-达鲺的常驻中毒被挂起（效果照记、维度暂清），窗口在下个黄昏收口后
    /// 按同一 EffectId 恢复（R-0047 第 1 / 3 条）。
    /// </summary>
    [Fact]
    public async Task BaristaHealthy_SuspendsPoison_AndRestoresItAfterWindow()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "no-dashii"), (2, "artist"), (3, "klutz"), (4, "mutant"), (5, "sage")),
            "test-barista-healthy-assign");
        Assert.Equal("Accepted", assigned.Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "JoinTraveller",
                null,
                "barista",
                "Good",
                null,
                "test-barista-healthy-join")).Kind);

        // 诺-达鲺的常驻中毒：相邻的 2 号（镇民）被毒。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => PoisonOf(host, 2) == PoisonState.Poisoned,
                Wait),
            "2 号没有被诺-达鲺毒到（常驻效果没有落地）");

        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barista-healthy-night-1");
        Assert.Equal("Accepted", nightOne.Kind);

        var decision = await WaitForSlotDecisionAsync(storyteller, "barista");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                decision.AwaitingDecisionId,
                "healthy:seat:2",
                null,
                "test-barista-healthy-rule")).Kind);

        // 窗口生效：中毒维度暂清，但中毒效果仍在账上（标记照记、暂不生效）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => PoisonOf(host, 2) == PoisonState.Healthy,
                Wait),
            "「清醒且健康」没有解除 2 号的中毒");
        Assert.Contains(
            host.Session.GetStorytellerView().PersistentEffects,
            effect => effect.Ability.Value.StartsWith("no-dashii", StringComparison.Ordinal) && !effect.IsTerminated);

        // 夜晚自然走完（首夜没有别的行动格）→ 白天 1 开完即关 → 次夜开始：窗口收口、中毒恢复。
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-barista-healthy-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-barista-healthy-close-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-barista-healthy-night-2")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => PoisonOf(host, 2) == PoisonState.Poisoned,
                Wait),
            "窗口结束后 2 号的中毒没有恢复");
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人视角下某席位的中毒维度（尚未观测时为 null）。</summary>
    private static PoisonState? PoisonOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .PoisonValue;

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
