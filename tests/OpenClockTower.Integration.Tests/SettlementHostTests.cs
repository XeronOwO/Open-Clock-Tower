using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 结算引擎在真实宿主里的行为（验收矩阵 1–8 行）：
/// 诺-达鲺的常驻中毒 → 被毒的筑梦师能力未生效 → 信息由说书人裁定并只下发给当事人 →
/// 来源死亡即解除并归因 → 两本账进状态账且重启仍在。
/// </summary>
/// <remarks>
/// 跑的是真宿主 + 真 SignalR + 真 SQLite；规则与判定都由生产代码路径完成，测试只驱动与断言。
/// </remarks>
public sealed class SettlementHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>矩阵 1 / 3 / 5 / 7（含信息隔离反方向）：中毒后能力未生效、信息由说书人给、来源死亡即解除、每格变化带效果链接。</summary>
    [Fact]
    public async Task PoisonedDreamer_IsIneffective_AndReleaseIsAttributed()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(
                (1, "no-dashii"),
                (2, "dreamer"),
                (3, "mutant"),
                (4, "klutz"),
                (5, "clockmaker")),
            "test-settle-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 常驻中毒：1 号诺-达鲺让顺时针的 2 号筑梦师与逆时针的 5 号钟表匠中毒；
        // 事实带 EffectId（矩阵 7）与归因（矩阵 1）。
        var poisonedView = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat =>
                seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
            Wait);
        Assert.NotNull(poisonedView);

        var poisonFact = Fact(poisonedView!, seat: 2, dimension: "Poison");
        Assert.Equal("Poisoned", poisonFact.Value);
        Assert.Equal(1, poisonFact.CausedBy);
        Assert.False(string.IsNullOrWhiteSpace(poisonFact.EffectId));

        // 矩阵 7：变化流里带同一条效果链接（面板对「这次变化」追问是哪条效果）。
        var poisonChange = Assert.Single(
            poisonedView!.RecentSeatChanges,
            change => change.Seat == 2 && change.Poison == "Poisoned");
        Assert.Equal(poisonFact.EffectId, poisonChange.EffectId);
        Assert.Contains(
            poisonedView!.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "no-dashii.poison"
                && effect.Target == 2
                && !effect.Terminated);
        Assert.Contains(
            poisonedView.Effects,
            effect => effect.Kind == "Persistent" && effect.Ability == "no-dashii.poison" && effect.Target == 5);

        InformationResultDto? delivered = null;
        OperationRequestDto? dreamerRequest = null;
        await using var dreamer = await host.ConnectSeatAsync(
            new SeatId(2),
            request => dreamerRequest = request,
            onInformation: information => delivered = information);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-settle-night-1");
        Assert.Equal("Accepted", started.Kind);

        // 钟表匠（5 号）是入口裁定点：信息由说书人给（D-0002）。
        var clockmakerDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
            Wait);
        Assert.NotNull(clockmakerDecision);
        await ResolveDecisionAsync(
            storyteller,
            clockmakerDecision!,
            "恶魔与最近的爪牙之间隔着 1 名玩家。",
            "test-settle-clockmaker");

        // 筑梦师请求：不能选自己（R-0007 未纳入旅行者）。
        var requested = await TestServerHost.WaitUntilAsync(() => dreamerRequest is not null, Wait);
        Assert.True(requested, "筑梦师在超时前没有收到操作请求");
        Assert.DoesNotContain("seat:2", dreamerRequest!.Options.Select(option => option.Value));
        var submitted = await dreamer.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            dreamerRequest.RequestId,
            "seat:1",
            "test-settle-dreamer-answer",
            1);
        Assert.Equal("Accepted", submitted.Kind);

        // 被毒的筑梦师：引擎不自动判定真假，改为再问一次说书人（矩阵 3）。
        var infoDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "dreamer",
            Wait);
        Assert.NotNull(infoDecision);
        Assert.Contains("未生效", infoDecision!.AwaitingDecisionContext, StringComparison.Ordinal);
        await ResolveDecisionAsync(
            storyteller,
            infoDecision,
            "1 号玩家可能是『涡流』。",
            "test-settle-info");

        // 信息只推给当事人；内容原样转达。
        var pushed = await TestServerHost.WaitUntilAsync(() => delivered is not null, Wait);
        Assert.True(pushed, "信息结果没有推送给筑梦师");
        Assert.Contains("涡流", delivered!.Content, StringComparison.Ordinal);

        // 「可能为假」与说书人说明都不下发（《重要细节》三-1：不要告诉玩家他醉酒或中毒）。
        var wire = JsonSerializer.Serialize(delivered);
        Assert.DoesNotContain("MayBeFalse", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Note", wire, StringComparison.OrdinalIgnoreCase);

        // 结算结论：能力未生效、分类中毒；两本账都进了状态账（矩阵 1 / 8 的账本侧）。
        var afterNight = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.NotNull(afterNight);
        Assert.NotNull(afterNight!.LastResolution);
        Assert.Equal(2, afterNight.LastResolution!.Seat);
        Assert.Equal("dreamer", afterNight.LastResolution.Ability);
        Assert.False(afterNight.LastResolution.Effective);
        Assert.Equal("Poisoned", afterNight.LastResolution.Malfunction);
        Assert.Contains(
            afterNight.AbilityUses,
            use => use.Seat == 2 && use.Ability == "dreamer" && !use.Effective);
        Assert.Contains(
            afterNight.Malfunctions,
            malfunction => malfunction.Seat == 2
                && malfunction.Ability == "dreamer"
                && malfunction.Kind == "Poisoned");

        // 反方向：重连的筑梦师能看到自己的信息；无关玩家（3 号）的重连包里没有它。
        await using var dreamerReconnect = await host.ConnectSeatAsync(new SeatId(2));
        Assert.Contains(
            host.Bundles[new SeatId(2)].View.InformationResults,
            information => information.Content.Contains("涡流", StringComparison.Ordinal));
        await using var bystander = await host.ConnectSeatAsync(new SeatId(3));
        Assert.Empty(host.Bundles[new SeatId(3)].View.InformationResults);

        // 矩阵 5：中毒来源死亡 → 效果终止（SourceDied）→ 维度解除并带效果链接。
        var killed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：诺-达鲺死亡",
            null,
            "test-settle-source-died");
        Assert.Equal("Accepted", killed.Kind);

        var releasedView = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat =>
                seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Healthy")),
            Wait);
        Assert.NotNull(releasedView);

        var releasedFact = Fact(releasedView!, seat: 2, dimension: "Poison");
        Assert.Equal("Healthy", releasedFact.Value);
        Assert.False(string.IsNullOrWhiteSpace(releasedFact.EffectId));
        Assert.Contains(
            releasedView!.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "no-dashii.poison"
                && effect.Target == 2
                && effect.Terminated
                && effect.TerminationKind == "SourceDied");
        var releaseChange = Assert.Single(
            releasedView.RecentSeatChanges,
            change => change.Seat == 2 && change.Poison == "Healthy" && change.EffectId is not null);
        Assert.Contains("解除", releaseChange.Reason, StringComparison.Ordinal);
        Assert.Equal(releasedFact.EffectId, releaseChange.EffectId);
    }

    /// <summary>矩阵 2 / 8：同时中毒且醉酒 = 不相互抵消、能力不生效（分类进待核对清单）；重启后账本仍在。</summary>
    [Fact]
    public async Task PoisonedAndDrunk_DoNotCancel_AndLedgersSurviveRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-settlement-{Guid.NewGuid():N}.db");

        await using (var first = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false,
            autoStartTestNight: false))
        {
            await using var storyteller = await first.ConnectStorytellerAsync();
            OperationRequestDto? dreamerRequest = null;
            OperationRequestDto? demonRequest = null;
            await using var demon = await first.ConnectSeatAsync(new SeatId(1), request => demonRequest = request);
            await using var dreamer = await first.ConnectSeatAsync(new SeatId(2), request => dreamerRequest = request);

            var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats(
                    (1, "no-dashii"),
                    (2, "dreamer"),
                    (3, "mutant"),
                    (4, "klutz"),
                    (5, "clockmaker")),
                "test-settle-drunk-assign");
            Assert.Equal("Accepted", assigned.Kind);

            // 第一夜先跑一遍（此时只有中毒），把筑梦师的能力结算走完。
            await RunNightAsync(
                storyteller,
                dreamer,
                demon,
                night: 1,
                dreamerRequest: () => dreamerRequest,
                demonRequest: () => demonRequest,
                keyPrefix: "test-settle-drunk-night-1");

            var afterFirstNight = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
            Assert.NotNull(afterFirstNight);
            Assert.Equal("Poisoned", afterFirstNight!.LastResolution!.Malfunction);

            // 上报醉酒：中毒 + 醉酒并存（《重要细节》三-3 不相互抵消）。
            var drunk = await storyteller.InvokeAsync<CommandResultDto>(
                "ReportSeatState",
                2,
                null,
                null,
                null,
                "Drunk",
                null,
                "测试：筑梦师醉酒",
                null,
                "test-settle-drunk-report");
            Assert.Equal("Accepted", drunk.Kind);

            dreamerRequest = null;
            demonRequest = null;
            await RunNightAsync(
                storyteller,
                dreamer,
                demon,
                night: 2,
                dreamerRequest: () => dreamerRequest,
                demonRequest: () => demonRequest,
                keyPrefix: "test-settle-drunk-night-2");

            var afterSecondNight = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
            Assert.NotNull(afterSecondNight);
            Assert.False(afterSecondNight!.LastResolution!.Effective);
            Assert.Equal("Open", afterSecondNight.LastResolution.Malfunction);
            Assert.Contains("同时中毒且醉酒", afterSecondNight.LastResolution.Note, StringComparison.Ordinal);

            var seatTwo = afterSecondNight.Seats.Single(seat => seat.Seat == 2);
            Assert.Equal("Poisoned", Fact(seatTwo, "Poison").Value);
            Assert.Equal("Drunk", Fact(seatTwo, "Drunk").Value);
        }

        // 重启：两本账与状态账都按事件流重放恢复（矩阵 8）。
        await using var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 5,
            databasePath: databasePath,
            autoStartTestNight: false);
        await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();

        var restored = await TestServerHost.WaitForViewAsync(
            storytellerAfterRestart,
            view => view.AbilityUses.Length > 0,
            Wait);
        Assert.NotNull(restored);
        Assert.Contains(
            restored!.AbilityUses,
            use => use.Seat == 2 && use.Ability == "dreamer" && !use.Effective);
        Assert.Contains(
            restored.Malfunctions,
            malfunction => malfunction.Seat == 2 && malfunction.Kind == "Open");
        var restoredSeatTwo = restored.Seats.Single(seat => seat.Seat == 2);
        Assert.Equal("Poisoned", Fact(restoredSeatTwo, "Poison").Value);
        Assert.Equal("Drunk", Fact(restoredSeatTwo, "Drunk").Value);
        // 生效中的效果本身也随事件流恢复（未终止）。
        Assert.Contains(
            restored.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "no-dashii.poison"
                && effect.Target == 2
                && !effect.Terminated);
    }

    /// <summary>
    /// 矩阵 4：对中毒玩家使用的能力仍然正常生效（健康的筑梦师查中毒的恶魔照常拿到信息）；
    /// 同一场景顺带钉住 R-0012 的挂起方向——来源中毒 → 维度解除且效果不终止 → 来源恢复 → 同标识重挂。
    /// </summary>
    [Fact]
    public async Task HealthyDreamerTargetingPoisonedDemon_IsEffective()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 7,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(
                (1, "no-dashii"),
                (2, "savant"),
                (3, "dreamer"),
                (4, "mutant"),
                (5, "klutz"),
                (6, "artist"),
                (7, "clockmaker")),
            "test-settle-symmetric-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 中毒的是 2 号与 7 号，不是筑梦师（3 号）。
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(seat =>
                seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
            Wait);
        Assert.NotNull(view);
        Assert.DoesNotContain(
            view!.Seats.Single(seat => seat.Seat == 3).Facts,
            fact => fact.Dimension == "Poison" && fact.Value == "Poisoned");

        OperationRequestDto? dreamerRequest = null;
        InformationResultDto? delivered = null;
        await using var dreamer = await host.ConnectSeatAsync(
            new SeatId(3),
            request => dreamerRequest = request,
            onInformation: information => delivered = information);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-settle-symmetric-night");
        Assert.Equal("Accepted", started.Kind);

        // 说书人上报恶魔中毒（对中毒玩家使用的能力这一行的前提）。
        // 阶段已开始，命令才被四道闸放行；同时这条上报让诺-达鲺的能力挂起（R-0012），
        // 它原来的两个中毒目标被解除、但效果本身不终止。
        var poisonedDemon = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            null,
            null,
            null,
            "Poisoned",
            "测试：恶魔中毒",
            3,
            "test-settle-poison-demon");
        Assert.Equal("Accepted", poisonedDemon.Kind);

        var suspended = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(seat =>
                seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Healthy")),
            Wait);
        Assert.NotNull(suspended);
        Assert.Contains(
            suspended!.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "no-dashii.poison"
                && effect.Target == 2
                && !effect.Terminated);
        var suspendedFact = Fact(suspended, seat: 2, dimension: "Poison");
        Assert.Equal("Healthy", suspendedFact.Value);

        // 挂起 ≠ 终止：来源恢复健康后同一条效果继续生效（R-0012），标识不变。
        var recovered = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            null,
            null,
            null,
            "Healthy",
            "测试：恶魔恢复健康",
            3,
            "test-settle-demon-recovered");
        Assert.Equal("Accepted", recovered.Kind);

        var restored = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(seat =>
                seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
            Wait);
        Assert.NotNull(restored);
        Assert.Equal(suspendedFact.EffectId, Fact(restored!, seat: 2, dimension: "Poison").EffectId);
        Assert.Contains(
            restored!.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "no-dashii.poison"
                && effect.Target == 2
                && !effect.Terminated);

        var clockmakerDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null && candidate.CurrentSlotId == "clockmaker",
            Wait);
        Assert.NotNull(clockmakerDecision);
        await ResolveDecisionAsync(storyteller, clockmakerDecision!, "距离 2。", "test-settle-symmetric-clockmaker");

        var requested = await TestServerHost.WaitUntilAsync(() => dreamerRequest is not null, Wait);
        Assert.True(requested, "筑梦师在超时前没有收到操作请求");
        var submitted = await dreamer.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            dreamerRequest!.RequestId,
            "seat:1",
            "test-settle-symmetric-answer",
            1);
        Assert.Equal("Accepted", submitted.Kind);

        // 目标（恶魔）是邪恶角色 → 说书人从善良角色里挑错误项（13 镇民 + 4 外来者）。
        var infoDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null && candidate.CurrentSlotId == "dreamer",
            Wait);
        Assert.NotNull(infoDecision);
        Assert.NotNull(infoDecision!.AwaitingDecisionOptions);
        Assert.Equal(17, infoDecision.AwaitingDecisionOptions!.Length);
        Assert.Contains(infoDecision.AwaitingDecisionOptions, option => option.Value == "artist");
        Assert.DoesNotContain(infoDecision.AwaitingDecisionOptions, option => option.Value == "no-dashii");
        await ResolveDecisionAsync(storyteller, infoDecision, "artist", "test-settle-symmetric-info");

        var afterNight = await TestServerHost.WaitForViewAsync(storyteller, candidate => candidate.PlanCompleted, Wait);
        Assert.NotNull(afterNight);
        Assert.True(afterNight!.LastResolution!.Effective, "健康的筑梦师查中毒的恶魔应当正常生效");
        Assert.Equal("dreamer", afterNight.LastResolution.Ability);
        Assert.Equal(3, afterNight.LastResolution.Seat);
        Assert.Contains(
            afterNight.AbilityUses,
            use => use.Seat == 3 && use.Effective);

        // 「仍得正确阵营」的证据：目标（中毒的恶魔）的真实角色照常出现在信息里。
        var informed = await TestServerHost.WaitUntilAsync(() => delivered is not null, Wait);
        Assert.True(informed, "信息结果没有推送给筑梦师");
        Assert.Contains("诺-达鲺", delivered!.Content, StringComparison.Ordinal);
        Assert.Contains("艺术家", delivered.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// 跑一夜：处理钟表匠裁定（仅首夜有钟表匠槽位）、筑梦师请求与它的选择后裁定；
    /// 第二夜还要先处理恶魔的击杀请求。
    /// </summary>
    private static async Task RunNightAsync(
        HubConnection storyteller,
        HubConnection dreamer,
        HubConnection demon,
        int night,
        Func<OperationRequestDto?> dreamerRequest,
        Func<OperationRequestDto?> demonRequest,
        string keyPrefix)
    {
        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            night,
            "Original",
            $"{keyPrefix}-start");
        Assert.Equal("Accepted", started.Kind);

        if (night > 1)
        {
            var demonAsked = await TestServerHost.WaitUntilAsync(() => demonRequest() is not null, Wait);
            Assert.True(demonAsked, $"第 {night} 夜恶魔没有收到操作请求");
            var killed = await demon.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                demonRequest()!.RequestId,
                "seat:3",
                $"{keyPrefix}-demon-answer",
                1);
            Assert.Equal("Accepted", killed.Kind);
        }

        if (night == 1)
        {
            var clockmakerDecision = await TestServerHost.WaitForViewAsync(
                storyteller,
                view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
                Wait);
            Assert.NotNull(clockmakerDecision);
            await ResolveDecisionAsync(storyteller, clockmakerDecision!, "距离 1。", $"{keyPrefix}-clockmaker");
        }

        var asked = await TestServerHost.WaitUntilAsync(() => dreamerRequest() is not null, Wait);
        Assert.True(asked, $"第 {night} 夜筑梦师没有收到操作请求");
        var submitted = await dreamer.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            dreamerRequest()!.RequestId,
            "seat:1",
            $"{keyPrefix}-dreamer-answer",
            1);
        Assert.Equal("Accepted", submitted.Kind);

        var infoDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "dreamer",
            Wait);
        Assert.NotNull(infoDecision);
        await ResolveDecisionAsync(storyteller, infoDecision!, "1 号玩家的信息由我裁定。", $"{keyPrefix}-info");
    }

    private static async Task ResolveDecisionAsync(
        HubConnection storyteller,
        StorytellerViewDto view,
        string decision,
        string key)
    {
        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            view.AwaitingDecisionId,
            decision,
            null,
            key);
        Assert.Equal("Accepted", resolved.Kind);
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];

    private static SeatStateFactDto Fact(StorytellerViewDto view, int seat, string dimension) =>
        Fact(view.Seats.Single(item => item.Seat == seat), dimension);

    private static SeatStateFactDto Fact(SeatStateDto seat, string dimension) =>
        seat.Facts.Single(fact => fact.Dimension == dimension);
}
