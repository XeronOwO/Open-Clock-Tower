using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集骨者重获**夜晚能力**的真宿主链路（票据 `traveller-and-exile` D5 第三批）：旅行者加入 →
/// 其他夜晚的黄昏槽 → 选择一名已死亡的玩家 → 该玩家**保持死亡**但当晚被唤醒使用刚恢复的能力 →
/// 下个黄昏失去能力。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《集骨者》· 2026-10-04 抓取（角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0054。
/// <para>
/// 重获**白天**能力那一族（杂耍艺人的公开猜测入口）在
/// <see cref="BoneCollectorDayEntryHostTests"/>——2026-10-05 按单文件 600 行门禁拆出。
/// </para>
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

        // 第 2 夜的恶魔击杀格要有人提交（见下面"越过恶魔击杀格"那一段）：这里的恶魔是 1 号。
        OperationRequestDto? demonRequest = null;
        await using var demon = await host.ConnectSeatAsync(new SeatId(1), asked => demonRequest = asked);

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
        // 前面那一格是恶魔击杀：照常由恶魔本人提交（点名**已死的 2 号**，不改变任何状态），
        // 不用强推越过——强推会把"恶魔格没人应答"这件事藏起来（本文件第三个用例踩过这个坑）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => demonRequest?.RequestId.Contains("fang-gu", StringComparison.Ordinal) == true,
                Wait),
            $"恶魔没有收到第 2 夜的击杀请求（最后：{demonRequest?.RequestId ?? "无"}）");
        var demonKill = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            demonRequest!.RequestId,
            "seat:2",
            "test-bone-demon-kill",
            1L);
        Assert.True(
            demonKill.Kind == "Accepted",
            $"恶魔的击杀被拒：{demonKill.RejectionCode} {demonKill.RejectionMessage}");

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

    /// <summary>
    /// 被重获的角色是「首个夜晚」能力（钟表匠）：它的行动格**不在**其他夜晚顺序表上，
    /// 因此平台按 R-0055 **追加**一格——死亡的他当夜被唤醒、拿到信息，且信息只到他本人。
    /// </summary>
    /// <remarks>
    /// 依据：百科《集骨者》· 角色简介 1（「『在你的首个夜晚』……的能力……可以在黄昏之前再次使用」）；
    /// 百科《重要细节》· 七（游戏中途被创造的「首个夜晚」角色应尽快结算、且排在致死效果之后）。
    /// </remarks>
    [Fact]
    public async Task BoneCollector_RegainingFirstNightAbility_AppendsSlotAndWakesTheDeadPlayer()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "clockmaker"), (3, "sage"), (4, "klutz"), (5, "mutant")),
            "test-bone-entry-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "bone-collector",
            "Good",
            null,
            "test-bone-entry-join");
        Assert.Equal("Accepted", joined.Kind);
        Assert.Equal(6, joined.IssuedSeat);

        OperationRequestDto? collectorRequest = null;
        await using var collector = await host.ConnectSeatAsync(new SeatId(6), asked => collectorRequest = asked);

        // 第 2 夜的恶魔击杀格同理：由恶魔（1 号）本人提交，不靠强推越过。
        OperationRequestDto? demonRequest = null;
        await using var demon = await host.ConnectSeatAsync(new SeatId(1), asked => demonRequest = asked);

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-bone-entry-night-1")).Kind);

        // 首夜：钟表匠本人在首夜表上（与集骨者无关），说书人给出他的信息，首夜才能走完。
        var firstNightInfo = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker" && view.AwaitingDecisionId is not null,
            Wait);
        Assert.NotNull(firstNightInfo);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                firstNightInfo!.AwaitingDecisionId,
                "恶魔离最近的爪牙有 1 名玩家",
                null,
                "test-bone-entry-night-1-info")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");

        // 白天 1：钟表匠死亡（死因与本测试无关，只要在集骨者行动前已死亡）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-bone-entry-day-1")).Kind);
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
                "测试：让钟表匠先死亡",
                null,
                "test-bone-entry-kill")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-bone-entry-close-day-1")).Kind);

        // 第 2 夜：集骨者选中已死亡的钟表匠。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-bone-entry-night-2")).Kind);
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
                "test-bone-entry-regain",
                1L)).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetStorytellerView().PersistentEffects.Any(effect =>
                    effect.Ability == new AbilityId("bone-collector.regain")
                    && effect.Target == new SeatId(2)
                    && effect.GrantedCharacter == new CharacterId("clockmaker")
                    && !effect.IsTerminated),
                Wait),
            "没有落重获能力窗口");

        // 计划里**追加**了一格：位置在最后一条可能致死的行动格（原本口径下最后一个恶魔 = 涡流）之后，
        // 行动者是保持死亡的 2 号，能力算在钟表匠名下。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var phase = stored.Select(item => item.Event).OfType<PhaseStartedEvent>()
            .Single(started => started.Plan.Label == "sv:night-2");
        var inserted = Assert.Single(stored.Select(item => item.Event).OfType<SlotInsertedEvent>());
        Assert.Equal(new SeatId(2), inserted.Slot.Actor);
        Assert.Equal(new CharacterId("clockmaker"), inserted.Slot.Owner);
        Assert.Equal(StepSlotKind.Action, inserted.Slot.Kind);
        Assert.Equal(new CharacterId("vortox"), phase.Plan.Slots[inserted.Index - 1].Character);
        Assert.Equal(
            phase.Plan.Slots.ToList().FindIndex(slot => slot.Id.Value == "vortox") + 1,
            inserted.Index);

        // 当夜：那一格被进入 → 说书人收到归属 2 号的裁定点（他虽死亡，仍被唤醒使用刚恢复的能力）。
        // 恶魔击杀格排在前面：由恶魔本人提交（点名**已死的 2 号**，不改变任何状态）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => demonRequest?.RequestId.Contains("fang-gu", StringComparison.Ordinal) == true,
                Wait),
            $"恶魔没有收到第 2 夜的击杀请求（最后：{demonRequest?.RequestId ?? "无"}）");
        var demonKill = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            demonRequest!.RequestId,
            "seat:2",
            "test-bone-entry-demon-kill",
            1L);
        Assert.True(
            demonKill.Kind == "Accepted",
            $"恶魔的击杀被拒：{demonKill.RejectionCode} {demonKill.RejectionMessage}");

        var woken = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker@2" && view.AwaitingDecisionSeat == 2,
            Wait);
        Assert.True(
            woken is { AwaitingDecisionId: not null },
            $"死亡的钟表匠没有被唤醒（最后 slot={woken?.CurrentSlotId ?? "无"} "
                + $"归属={woken?.AwaitingDecisionSeat?.ToString() ?? "无"}）");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                woken!.AwaitingDecisionId,
                "恶魔离最近的爪牙有 2 名玩家",
                null,
                "test-bone-entry-clockmaker-info")).Kind);

        // 信息只到 2 号本人：本局其他席位一条信息都没有。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(2)).InformationResults
                    .Any(result => result.Ability.Value == "clockmaker"),
                Wait),
            "重获的钟表匠没有拿到信息");
        Assert.Empty(host.Session.GetPlayerView(new SeatId(3)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(4)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(5)).InformationResults);

        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 2 夜没有自然走完");
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
