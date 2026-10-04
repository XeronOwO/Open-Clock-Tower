using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集骨者的真宿主链路（票据 `traveller-and-exile` D5 第三批）：旅行者加入 → 其他夜晚的黄昏槽 →
/// 选择一名已死亡的玩家 → 该玩家**保持死亡**但当晚被唤醒使用刚恢复的能力 → 下个黄昏失去能力。
/// 跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《集骨者》· 2026-10-04 抓取（角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0054。
/// </remarks>
public sealed class BoneCollectorHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 卖花女孩（只在他夜行动）先死亡；第 2 夜集骨者选中她 → 重获窗口落账、她在当夜被唤醒
    /// （说书人收到归属她席位的裁定点）→ 第 3 夜黄昏窗口收口，她不再次被唤醒。
    /// </summary>
    [Fact]
    public async Task BoneCollector_RegainsDeadPlayersAbility_UntilNextDusk()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "flowergirl"), (3, "sage"), (4, "klutz"), (5, "mutant")),
            "test-bone-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "bone-collector",
            "Good",
            null,
            "test-bone-join");
        Assert.True(
            joined.Kind == "Accepted",
            $"加入集骨者被拒：{joined.RejectionCode} {joined.RejectionMessage}");
        Assert.Equal(6, joined.IssuedSeat);

        OperationRequestDto? collectorRequest = null;
        await using var collector = await host.ConnectSeatAsync(new SeatId(6), asked => collectorRequest = asked);

        // 首夜：集骨者不行动（夜序表只把旅行者黄昏行动放其他夜晚）；这一桌也没有首夜行动 → 夜自然走完。
        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-bone-night-1");
        Assert.Equal("Accepted", nightOne.Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");
        Assert.Null(collectorRequest);

        // 白天 1：2 号（卖花女孩）死亡（死因与本测试无关，只要在集骨者行动前已死亡）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-bone-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ReportSeatState",
                2,
                "Dead",
                null,
                null,
                null,
                null,
                "测试：让卖花女孩先死亡",
                null,
                "test-bone-kill")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-bone-close-day-1")).Kind);

        // 第 2 夜：黄昏槽唤醒集骨者，说书人（这里由玩家本人操作）选 2 号。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-bone-night-2")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => collectorRequest is not null && collectorRequest.RequestId == "sv:night-2:bone-collector",
                Wait),
            $"集骨者没有收到黄昏行动请求（最后：{collectorRequest?.RequestId ?? "无"}）");
        Assert.Equal(
            "Accepted",
            (await collector.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                collectorRequest!.RequestId,
                "seat:2",
                "test-bone-regain",
                1L)).Kind);

        // 重获窗口落账：目标 = 2 号（保持死亡）、被重获的角色 = 卖花女孩。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetStorytellerView().PersistentEffects.Any(effect =>
                    effect.Ability == new AbilityId("bone-collector.regain")
                    && effect.Target == new SeatId(2)
                    && effect.GrantedCharacter == new CharacterId("flowergirl")
                    && !effect.IsTerminated),
                Wait),
            "没有落重获能力窗口");

        // 当夜：卖花女孩的格（空槽）被激活——说书人收到归属 2 号的裁定点（她虽然死亡，仍被唤醒使用能力）。
        var fangGu = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "fang-gu",
            Wait);
        Assert.NotNull(fangGu);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过恶魔击杀格",
                "test-bone-force-fang-gu")).Kind);

        var woken = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "flowergirl" && view.AwaitingDecisionSeat == 2,
            Wait);
        Assert.True(
            woken is { AwaitingDecisionId: not null } && woken.CurrentSlotId == "flowergirl",
            $"死亡的卖花女孩没有被唤醒（最后 slot={woken?.CurrentSlotId ?? "无"} "
                + $"归属={woken?.AwaitingDecisionSeat?.ToString() ?? "无"}）");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                woken!.AwaitingDecisionId,
                "恶魔今天没有投票",
                null,
                "test-bone-flowergirl-info")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 2 夜没有自然走完");

        // 白天 2：开完即关 → 第 3 夜：下个黄昏收口，重获窗口终止。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-bone-day-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-bone-close-day-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                3,
                "Original",
                "test-bone-night-3")).Kind);

        var window = host.Session.GetStorytellerView().PersistentEffects.Single(effect =>
            effect.Ability == new AbilityId("bone-collector.regain"));
        Assert.True(window.IsTerminated, "重获窗口没有在下个黄昏收口");
        Assert.Contains("R-0054", window.Termination!.Reason, StringComparison.Ordinal);

        // 卖花女孩只被唤醒过一次：第 3 夜她不再获得新的信息（窗口已收口）。
        Assert.Equal(1, host.Session.GetPlayerView(new SeatId(2)).InformationResults.Count(result =>
            result.Ability.Value == "flowergirl"));
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
