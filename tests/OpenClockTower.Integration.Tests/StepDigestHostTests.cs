using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 每步摘要（StepDigest）在真宿主上的行为：行 1 / 2（行动者状态及归因 + 能力未生效）、
/// 行 5（解除原因进摘要）、行 6（依赖失效作废说明进摘要），外加「已结算优先」与重启恢复。
/// </summary>
/// <remarks>
/// 跑真宿主 + 真 SignalR + 真 SQLite；界面呈现由验收批次 tools/verify-storyteller-panel.mjs 判。
/// 槽位配额给足（3600s）时用 ForceAdvance 精确推进到目标槽位，避免"等配额"与"抢结算窗口"的时序侥幸。
/// </remarks>
public sealed class StepDigestHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Digests_ShowActorStateAndAbilityConclusion()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-digest-{Guid.NewGuid():N}.db");
        try
        {
            await using (var host = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false))
            {
                await using var storyteller = await host.ConnectStorytellerAsync();
                var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                    "AssignCharacters",
                    Seats((1, "no-dashii"), (2, "dreamer"), (3, "mutant"), (4, "klutz"), (5, "clockmaker")),
                    "test-digest-assign");
                Assert.Equal("Accepted", assigned.Kind);

                var poisoned = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.Seats.Any(seat => seat.Seat == 5
                        && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
                    Wait);
                Assert.NotNull(poisoned);

                var drunk = await storyteller.InvokeAsync<CommandResultDto>(
                    "ReportSeatState",
                    5,
                    null,
                    null,
                    null,
                    "Drunk",
                    null,
                    "测试：钟表匠醉酒",
                    null,
                    "test-digest-drunk");
                Assert.Equal("Accepted", drunk.Kind);

                OperationRequestDto? dreamerRequest = null;
                await using var dreamer = await host.ConnectSeatAsync(new SeatId(2), request => dreamerRequest = request);

                var started = await storyteller.InvokeAsync<CommandResultDto>(
                    "StartNight",
                    1,
                    "Original",
                    "test-digest-night");
                Assert.Equal("Accepted", started.Kind);

                // 真实顺序表前 9 个是节拍 / 空槽位（含 D5 的咖啡师黄昏槽）；用兜底强推精确推进
                // （配额 3600s 不会被等走）。
                for (var step = 0; step < 9; step++)
                {
                    var advanced = await storyteller.InvokeAsync<CommandResultDto>(
                        "ForceAdvance",
                        "测试：快进到钟表匠槽位",
                        $"test-digest-force-{step}");
                    Assert.Equal("Accepted", advanced.Kind);
                }

                // 行 2：钟表匠槽位是没有玩家选项的入口裁定点——摘要要同时给出中毒 + 醉酒与未生效（R-0004）。
                var clockmakerDecision = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
                    Wait);
                Assert.NotNull(clockmakerDecision);
                var clockmakerDigest = Assert.IsType<StepDigestDto>(clockmakerDecision!.StepDigest);
                Assert.Equal(5, clockmakerDigest.Seat);
                Assert.Equal("clockmaker", clockmakerDigest.Character);
                Assert.Equal("Poisoned", Fact(clockmakerDigest.State!, "Poison").Value);
                Assert.Equal(1, Fact(clockmakerDigest.State!, "Poison").CausedBy);
                Assert.False(string.IsNullOrWhiteSpace(Fact(clockmakerDigest.State!, "Poison").EffectId));
                Assert.Equal("Drunk", Fact(clockmakerDigest.State!, "Drunk").Value);
                Assert.Equal(0, clockmakerDigest.OptionCount);
                Assert.Equal("StorytellerDecides", clockmakerDigest.OnNoOption);
                var clockmakerAbility = Assert.IsType<SlotAbilityDto>(clockmakerDigest.Ability);
                Assert.Equal("Preview", clockmakerAbility.Basis);
                Assert.Equal("clockmaker", clockmakerAbility.Ability);
                Assert.False(clockmakerAbility.Effective);
                Assert.Equal(new[] { "Poisoned", "Drunk" }, clockmakerAbility.Malfunctions);
                Assert.Contains("同时中毒且醉酒", clockmakerAbility.Note!, StringComparison.Ordinal);

                // 结清钟表匠裁定点：配额 3600s，槽位仍停在原处——摘要要切换成「已结算」。
                await ResolveDecisionAsync(storyteller, clockmakerDecision, "距离 2。", "test-digest-clockmaker");
                var settledClockmaker = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.CurrentSlotId == "clockmaker" && view.StepDigest?.Ability?.Basis == "Settled",
                    Wait);
                Assert.NotNull(settledClockmaker);
                Assert.Equal(new[] { "Poisoned", "Drunk" }, settledClockmaker!.StepDigest!.Ability!.Malfunctions);
                Assert.NotNull(settledClockmaker.StepDigest.Ability.Sequence);

                var advancedToDreamer = await storyteller.InvokeAsync<CommandResultDto>(
                    "ForceAdvance",
                    "测试：推进到筑梦师槽位",
                    "test-digest-force-dreamer");
                Assert.Equal("Accepted", advancedToDreamer.Kind);

                // 行 1：筑梦师槽位有玩家选项——摘要给出中毒来源与「能力未生效（中毒）」。
                var asked = await TestServerHost.WaitUntilAsync(() => dreamerRequest is not null, Wait);
                Assert.True(asked, "筑梦师没有收到操作请求");
                var dreamerView = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.CurrentSlotId == "dreamer" && view.StepDigest is not null,
                    Wait);
                Assert.NotNull(dreamerView);
                var dreamerDigest = dreamerView!.StepDigest!;
                Assert.Equal(2, dreamerDigest.Seat);
                Assert.Equal("dreamer", dreamerDigest.Character);
                Assert.Equal("Poisoned", Fact(dreamerDigest.State!, "Poison").Value);
                Assert.Equal(1, Fact(dreamerDigest.State!, "Poison").CausedBy);
                Assert.True(dreamerDigest.OptionCount > 0);
                var dreamerAbility = Assert.IsType<SlotAbilityDto>(dreamerDigest.Ability);
                Assert.Equal("Preview", dreamerAbility.Basis);
                Assert.Equal("dreamer", dreamerAbility.Ability);
                Assert.False(dreamerAbility.Effective);
                Assert.Equal(new[] { "Poisoned" }, dreamerAbility.Malfunctions);

                // 作答 → 未生效 → 说书人自由裁定 → 结算；摘要再次转为「已结算」。
                var submitted = await dreamer.InvokeAsync<CommandResultDto>(
                    "SubmitResponse",
                    dreamerRequest!.RequestId,
                    "seat:1",
                    "test-digest-answer",
                    1);
                Assert.Equal("Accepted", submitted.Kind);
                var infoDecision = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "dreamer",
                    Wait);
                Assert.NotNull(infoDecision);
                await ResolveDecisionAsync(storyteller, infoDecision!, "1 号玩家可能是『涡流』。", "test-digest-info");
                var settledDreamer = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.CurrentSlotId == "dreamer" && view.StepDigest?.Ability?.Basis == "Settled",
                    Wait);
                Assert.NotNull(settledDreamer);
                Assert.False(settledDreamer!.StepDigest!.Ability!.Effective);
                Assert.Equal(new[] { "Poisoned" }, settledDreamer.StepDigest.Ability.Malfunctions);
            }

            // 重启恢复：逐槽位结算跟踪必须从事件流重建（槽位仍停在已结算的筑梦师）。
            await using var restarted = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 5,
                databasePath: databasePath,
                autoStartTestNight: false);
            await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
            var restored = await TestServerHost.WaitForViewAsync(
                storytellerAfterRestart,
                view => view.CurrentSlotId == "dreamer" && view.StepDigest?.Ability?.Basis == "Settled",
                Wait);
            Assert.NotNull(restored);
            Assert.Equal(2, restored!.StepDigest!.Seat);
            Assert.Equal(new[] { "Poisoned" }, restored.StepDigest.Ability!.Malfunctions);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task HeldSlot_ShowsReleaseReason_InDigest()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.1, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "no-dashii"), (2, "dreamer"), (3, "mutant"), (4, "klutz"), (5, "clockmaker")),
            "test-digest-release-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var poisoned = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat => seat.Seat == 5
                && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
            Wait);
        Assert.NotNull(poisoned);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-digest-release-night");
        Assert.Equal("Accepted", started.Kind);

        var decision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
            Wait);
        Assert.NotNull(decision);
        Assert.True(
            decision!.StepDigest?.State?.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned"),
            "裁定点挂起时摘要应已带行动者的中毒事实");

        // 中毒来源死亡：常驻效果终止 → 维度解除事件落在 5 号；槽位还被裁定点挂着，摘要必须给出解除原因。
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
            "test-digest-release-kill");
        Assert.Equal("Accepted", killed.Kind);

        var released = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker"
                && view.StepDigest?.State?.Facts.Any(fact =>
                    fact.Dimension == "Poison"
                    && fact.Value == "Healthy"
                    && fact.Reason.Contains("解除", StringComparison.Ordinal)) == true,
            Wait);
        Assert.NotNull(released);

        var healthy = Fact(released!.StepDigest!.State!, "Poison");
        Assert.Contains("解除", healthy.Reason, StringComparison.Ordinal);
        Assert.Equal(1, healthy.CausedBy);
        Assert.False(string.IsNullOrWhiteSpace(healthy.EffectId));

        // 解除后预览跟着翻正：同一个判定器、同一份账，不是前端推断。
        Assert.Equal("Preview", released.StepDigest.Ability?.Basis);
        Assert.True(released.StepDigest.Ability!.Effective);
    }

    [Fact]
    public async Task DependencyViolation_VoidsRequest_AndSummaryShowsWhichDependency()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-digest-void-{Guid.NewGuid():N}.db");
        try
        {
            await using (var host = new TestServerHost(
                slotQuotaSeconds: 0.1,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false))
            {
                await using var storyteller = await host.ConnectStorytellerAsync();
                var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                    "AssignCharacters",
                    Seats((1, "no-dashii"), (2, "dreamer"), (3, "mutant"), (4, "klutz"), (5, "clockmaker")),
                    "test-digest-void-assign");
                Assert.Equal("Accepted", assigned.Kind);

                var poisoned = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.Seats.Any(seat => seat.Seat == 5
                        && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
                    Wait);
                Assert.NotNull(poisoned);

                OperationRequestDto? dreamerRequest = null;
                await using var dreamer = await host.ConnectSeatAsync(new SeatId(2), request => dreamerRequest = request);
                var started = await storyteller.InvokeAsync<CommandResultDto>(
                    "StartNight",
                    1,
                    "Original",
                    "test-digest-void-night");
                Assert.Equal("Accepted", started.Kind);

                var decision = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
                    Wait);
                Assert.NotNull(decision);
                await ResolveDecisionAsync(storyteller, decision!, "距离 1。", "test-digest-void-clockmaker");

                var asked = await TestServerHost.WaitUntilAsync(() => dreamerRequest is not null, Wait);
                Assert.True(asked, "筑梦师没有收到操作请求");

                // 上游依赖变化：挂起请求的行动者自己死亡 → 自动作废，说明写明哪条依赖不满足。
                var killed = await storyteller.InvokeAsync<CommandResultDto>(
                    "ReportSeatState",
                    2,
                    "Dead",
                    null,
                    null,
                    null,
                    null,
                    "测试：筑梦师死亡",
                    null,
                    "test-digest-void-kill");
                Assert.Equal("Accepted", killed.Kind);

                var voided = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.LastVoidedRequest is not null,
                    Wait);
                Assert.NotNull(voided);
                Assert.Equal(dreamerRequest!.RequestId, voided!.LastVoidedRequest!.RequestId);
                Assert.Equal("DependencyViolated", voided.LastVoidedRequest.Reason);
                Assert.Contains("座位 2", voided.LastVoidedRequest.Note!, StringComparison.Ordinal);
                Assert.Contains("生死 Dead ≠ 要求 Alive", voided.LastVoidedRequest.Note!, StringComparison.Ordinal);
            }

            // 重启：最近作废是事件流派生量，必须能恢复（否则说书人重启后看不到请求为什么没了）。
            await using var restarted = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 5,
                databasePath: databasePath,
                autoStartTestNight: false);
            await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
            var restored = await TestServerHost.WaitForViewAsync(
                storytellerAfterRestart,
                view => view.LastVoidedRequest is not null,
                Wait);
            Assert.NotNull(restored);
            Assert.Equal("DependencyViolated", restored!.LastVoidedRequest!.Reason);
            Assert.Contains("生死 Dead ≠ 要求 Alive", restored.LastVoidedRequest.Note!, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void IncompleteLedger_IsUnknown_NotGuessed()
    {
        var machine = StepMachine.StartPhase(TestNightPlan.CreateFirstNight(seatCount: 3)).State;
        var partial = GameStateMachine.Apply(
            GameState.Empty,
            new SeatStateChangedEvent
            {
                Seat = new SeatId(1),
                Character = new CharacterId("test-character"),
                Reason = "测试：只观测到角色",
            });

        var digest = StepDigestProjection.Build(machine, partial, slotAbility: null, slotResolution: null);

        Assert.NotNull(digest);
        Assert.Equal(new SeatId(1), digest!.Seat);
        Assert.Equal(2, digest.OptionCount);
        Assert.Equal(NoOptionBehavior.Skip, digest.OnNoOption);
        var ability = Assert.IsType<SlotAbilitySnapshot>(digest.Ability);
        Assert.Equal(SlotAbilityBasis.Unknown, ability.Basis);
        Assert.Null(ability.Effective);
        Assert.Contains("无法判定", ability.Note!, StringComparison.Ordinal);
    }

    private static async Task ResolveDecisionAsync(
        GameClient storyteller,
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

    private static SeatStateFactDto Fact(SeatStateDto state, string dimension) =>
        state.Facts.Single(fact => fact.Dimension == dimension);

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
