using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 非首个夜晚获得的「首个夜晚」能力在真宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0055）：
/// 哲学家第 2 夜获得钟表匠的能力 → 计划里**追加**一格 → 他被唤醒（说书人收到归属他的裁定点）→
/// 信息只到他本人。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 依据：百科《哲学家》· 范例 2——「在第三个夜晚，哲学家选择获得钟表匠的能力。当晚，
/// 他得知了恶魔与爪牙之间的最近的距离」；《重要细节》· 七——游戏中途获得的「首个夜晚」能力
/// 应尽快结算、且排在会造成死亡的效果之后。
/// </remarks>
public sealed class GrantedEntryAbilityHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>第 1 夜摇头不用 → 第 2 夜获得钟表匠 → 当夜追加一格并结算，信息只到哲学家。</summary>
    [Fact]
    public async Task PhilosopherGainsFirstNightAbilityOnSecondNight_AppendsSlotAndResolvesIt()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "philosopher"), (2, "klutz"), (3, "mutant"), (4, "barber"), (5, "sweetheart")),
            "test-entry-assign");
        Assert.Equal("Accepted", assigned.Kind);

        OperationRequestDto? grant = null;
        await using var philosopher = await host.ConnectSeatAsync(new SeatId(1), asked => grant = asked);

        // 第 1 夜：摇头不用（不消耗「每局限一次」）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-entry-night-1")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => grant is not null, Wait),
            "哲学家第 1 夜没有收到行动请求");
        Assert.Equal(
            "Accepted",
            (await philosopher.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                grant!.RequestId,
                "decline",
                "test-entry-decline",
                1L)).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 1 夜没有自然走完");

        // 白天 1：开完即关。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-entry-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-entry-close-day-1")).Kind);

        // 第 2 夜：获得钟表匠的能力（「首个夜晚」能力，其他夜晚表上没有它的格）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-entry-night-2")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => grant is not null && grant.RequestId == "sv:night-2:philosopher",
                Wait),
            $"哲学家没有收到第 2 夜的行动请求（最后：{grant?.RequestId ?? "无"}）");
        var nightTwo = grant!;
        Assert.Contains(nightTwo.Options, option => option.Value == "clockmaker");
        Assert.Equal(
            "Accepted",
            (await philosopher.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                nightTwo.RequestId,
                "clockmaker",
                "test-entry-grant",
                1L)).Kind);

        // 计划里**追加**了一格：落在最后一条可能致死的行动格（原本口径下最后一个恶魔 = 涡流）之后；
        // 行动者是哲学家本人，能力算在钟表匠名下（代行格）。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var phase = stored.Select(item => item.Event).OfType<PhaseStartedEvent>()
            .Single(started => started.Plan.Label == "sv:night-2");
        var inserted = Assert.Single(stored.Select(item => item.Event).OfType<SlotInsertedEvent>());
        Assert.Equal(new SeatId(1), inserted.Slot.Actor);
        Assert.Equal(new CharacterId("clockmaker"), inserted.Slot.Owner);
        Assert.Equal(new CharacterId("philosopher"), inserted.Slot.Character);
        Assert.Equal(
            phase.Plan.Slots.ToList().FindIndex(slot => slot.Id.Value == "vortox") + 1,
            inserted.Index);

        // 当夜：追加格被进入 → 说书人收到归属 1 号的裁定点（钟表匠的信息由说书人给出）。
        var woken = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker@1" && view.AwaitingDecisionSeat == 1,
            Wait);
        Assert.True(
            woken is { AwaitingDecisionId: not null },
            $"哲学家没有被追加唤醒（最后 slot={woken?.CurrentSlotId ?? "无"} "
                + $"归属={woken?.AwaitingDecisionSeat?.ToString() ?? "无"}）");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                woken!.AwaitingDecisionId,
                "恶魔离最近的爪牙有 3 名玩家",
                null,
                "test-entry-clockmaker-info")).Kind);

        // 信息只到哲学家本人：本局其他席位一条信息都没有。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(1)).InformationResults
                    .Any(result => result.Ability.Value == "clockmaker"),
                Wait),
            "哲学家没有拿到钟表匠的信息");
        Assert.Empty(host.Session.GetPlayerView(new SeatId(2)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(3)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(4)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(5)).InformationResults);

        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 2 夜没有自然走完");
    }

    /// <summary>
    /// 角色变更的四个入口（麻脸巫婆 / 理发师 / 舞蛇人 / 说书人手工上报）共用
    /// <c>NightSlotActivation.Plan</c>：这里取**手工上报**做运行时证据——把一名存活玩家改成钟表匠，
    /// 计划里出现追加格、他被唤醒并使用刚获得的进场能力。
    /// </summary>
    [Fact]
    public async Task ManualCharacterChangeIntoFirstNightAbility_AppendsSlot()
    {
        // 节拍放慢到 0.3s/槽：手工改角必须落在夜间计划**还没走完**的时候（整夜约 25 槽 ≈ 7.5s）。
        await using var host = new TestServerHost(slotQuotaSeconds: 0.3, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "klutz"), (2, "mutant"), (3, "barber"), (4, "sweetheart"), (5, "sage")),
            "test-entry-adopt-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));

        // 第 1 夜：这一桌没有夜间行动角色（理发师 / 心上人 / 贤者都是触发格）→ 自然走完。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 1, "Original", "test-entry-adopt-night-1")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 1 夜没有自然走完");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-entry-adopt-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-entry-adopt-close-day-1")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartNight", 2, "Original", "test-entry-adopt-night-2")).Kind);

        // 游戏中途把 1 号变成钟表匠（说书人手工上报角色维度）。
        var changed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Alive",
            "clockmaker",
            "Good",
            "Sober",
            "Healthy",
            "测试：把 1 号改成钟表匠（首个夜晚型能力，游戏中途被创造）",
            null,
            "test-entry-adopt-change");
        Assert.Equal("Accepted", changed.Kind);

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var phase = stored.Select(item => item.Event).OfType<PhaseStartedEvent>()
            .Single(started => started.Plan.Label == "sv:night-2");
        var inserted = Assert.Single(stored.Select(item => item.Event).OfType<SlotInsertedEvent>());
        Assert.Equal(new SeatId(1), inserted.Slot.Actor);
        Assert.Equal(new CharacterId("clockmaker"), inserted.Slot.Owner);
        Assert.Equal(new CharacterId("clockmaker"), inserted.Slot.Character);
        Assert.Equal(
            phase.Plan.Slots.ToList().FindIndex(slot => slot.Id.Value == "vortox") + 1,
            inserted.Index);

        var woken = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker@1" && view.AwaitingDecisionSeat == 1,
            Wait);
        Assert.True(
            woken is { AwaitingDecisionId: not null },
            $"改角后的钟表匠没有被唤醒（最后 slot={woken?.CurrentSlotId ?? "无"} "
                + $"归属={woken?.AwaitingDecisionSeat?.ToString() ?? "无"}）");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                woken!.AwaitingDecisionId,
                "恶魔离最近的爪牙有 4 名玩家",
                null,
                "test-entry-adopt-info")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(1)).InformationResults
                    .Any(result => result.Ability.Value == "clockmaker"),
                Wait),
            "改角后的钟表匠没有拿到信息");
        Assert.Empty(host.Session.GetPlayerView(new SeatId(2)).InformationResults);
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
