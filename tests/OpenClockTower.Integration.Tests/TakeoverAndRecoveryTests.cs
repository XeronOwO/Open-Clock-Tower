using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 行 21–24：接管、兜底与恢复（D-0014）在真实宿主上的证据。
/// </summary>
public sealed class TakeoverAndRecoveryTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(6);

    /// <summary>行 21：阻塞状态下说书人强推 → 游戏继续，阻塞清除。</summary>
    [Fact]
    public async Task BlockedSlot_ForceAdvance_ContinuesGame()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 2);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await CompleteTestNightAsync(storyteller);

        var plan = new StepPlan
        {
            Label = "test:blocked",
            Phase = GamePhase.OtherNight,
            Slots =
            [
                StepSlot.Action(
                    new StepSlotId("blocked-slot"),
                    new SeatId(1),
                    new ChoicePrompt
                    {
                        Context = "无合法选项（测试）",
                        Options = [],
                        OnNoOption = NoOptionBehavior.BlockAndAlert,
                    },
                    [new SeatDependency { Seat = new SeatId(1), RequiredLife = LifeState.Alive }]),
            ],
        };
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = plan },
            "test-blocked-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        var blocked = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(blocked.BlockedReason);

        var forced = await storyteller.InvokeAsync<CommandResultDto>(
            "ForceAdvance",
            "程序逻辑阻塞，人工兜底",
            "test-force-blocked");
        Assert.Equal("Accepted", forced.Kind);

        var after = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(after.BlockedReason);
        Assert.True(after.PlanCompleted || after.SlotIndex >= 1);
    }

    /// <summary>行 22：接管暂停自动节拍，手动强推 / 交还后恢复自动推进。</summary>
    [Fact]
    public async Task TakeOver_PausesAutomaticAdvance_ReleaseResumes()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 1.0, seatCount: 2);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await CompleteTestNightAsync(storyteller);

        var plan = new StepPlan
        {
            Label = "test:takeover",
            Phase = GamePhase.OtherNight,
            Slots =
            [
                StepSlot.Empty(new StepSlotId("empty-1")),
                StepSlot.DawnWait(new StepSlotId("dawn")),
            ],
        };
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = plan },
            "test-takeover-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        var takeover = await storyteller.InvokeAsync<CommandResultDto>("TakeOver", "说书人手动调板", "test-takeover-1");
        Assert.Equal("Accepted", takeover.Kind);

        await Task.Delay(1500);
        var paused = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal(0, paused.SlotIndex);
        Assert.Equal("StorytellerTakeover", paused.Control);

        var release = await storyteller.InvokeAsync<CommandResultDto>("ReleaseControl", "交还自动化", "test-release-1");
        Assert.Equal("Accepted", release.Kind);

        var resumed = await TestServerHost.WaitForViewAsync(storyteller, v => v.SlotIndex >= 1, Wait);
        Assert.NotNull(resumed);
        Assert.True(resumed!.SlotIndex >= 1);
    }

    /// <summary>行 23 / 24：重建修复不一致的派生数据；事件损坏时显式失败且兜底入口仍可用。</summary>
    [Fact]
    public async Task Rebuild_RepairsStaleSnapshot_AndFailsExplicitlyOnCorruptEvents()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 2);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<GameDbContext>>();

        // 弄脏快照：模拟"派生投影与事件不一致"
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var snapshot = await db.Snapshots.SingleAsync(item => item.GameId == "default");
            snapshot.MachineJson = "{\"stale\":true}";
            await db.SaveChangesAsync();
        }

        var rebuilt = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "投影与事件不一致",
            "test-rebuild-1");
        Assert.Equal("Accepted", rebuilt.Kind);
        Assert.True(rebuilt.MachineEquivalent);
        Assert.False(rebuilt.SnapshotEquivalent); // 旧快照损坏 → 必须明确报告"与事件流不一致"
        Assert.True(rebuilt.LedgerEquivalent); // 事件流干净 → 状态账也必须报等价

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var repaired = await db.Snapshots.SingleAsync(item => item.GameId == "default");
            Assert.DoesNotContain("stale", repaired.MachineJson ?? string.Empty);
        }

        // 事件是事实来源：重建后挂起请求仍在
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(view.Pending);

        // 注入未知事件：重建必须显式失败，不许静默继续
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Events.Add(new EventEntity
            {
                GameId = "default",
                Sequence = 999_999,
                Type = "NoSuchEvent",
                Payload = "{}",
                RecordedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var failed = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "数据异常",
            "test-rebuild-2");
        Assert.Equal("Failed", failed.Kind);
        Assert.Contains("未知事件类型", failed.Failure ?? string.Empty);
        Assert.Null(failed.LedgerEquivalent); // 失败回执不返回任何"等价"假结论

        // 行 24：重建失败不锁死房间——兜底入口仍可用
        var forced = await storyteller.InvokeAsync<CommandResultDto>(
            "ForceAdvance",
            "重建失败后兜底继续",
            "test-force-after-failure");
        Assert.Equal("Accepted", forced.Kind);
    }

    /// <summary>事件载荷损坏：宿主必须能启动（停在空状态、显式报错），且后续可显式重开阶段恢复。</summary>
    [Fact]
    public async Task CorruptEventPayload_RoomStartsEmpty_AndCanBeRevivedExplicitly()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-corrupt-{Guid.NewGuid():N}.db");
        try
        {
            var first = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 2,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false);
            await first.DisposeAsync();

            var options = new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using (var db = new GameDbContext(options))
            {
                var row = await db.Events.OrderBy(item => item.Sequence).FirstAsync();
                row.Payload = "{ this is not valid json";
                await db.SaveChangesAsync();
            }

            await using var revived = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 2,
                databasePath: databasePath,
                autoStartTestNight: false);
            Assert.Null(revived.Session.GetStorytellerView().Phase); // 停在空状态，不静默继续

            await using var storyteller = await revived.ConnectStorytellerAsync();
            var rebuild = await storyteller.InvokeAsync<CommandResultDto>(
                "RebuildRoom",
                "事件载荷损坏",
                "test-corrupt-rebuild");
            Assert.Equal("Failed", rebuild.Kind);
            Assert.Contains("事件载荷损坏", rebuild.Failure ?? string.Empty);

            // 兜底入口：宿主显式重开阶段（数据损失是显式的，不是静默继续）
            var restarted = await revived.ExecuteHostCommandAsync(
                new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(2) },
                "test-corrupt-restart",
                CancellationToken.None);
            Assert.Equal(CommandResultKind.Accepted, restarted.Kind);
            Assert.NotNull(revived.Session.GetStorytellerView().Phase);
        }
        finally
        {
            DeleteFiles(databasePath);
        }
    }

    /// <summary>
    /// 重建票行 1–3：干净流报"账等价"；派生的内存账与事件流分叉时报不一致，并由重建修回；
    /// 事件流损坏时显式失败、不返回任何"等价"结论（在 <see cref="Rebuild_RepairsStaleSnapshot_AndFailsExplicitlyOnCorruptEvents"/> 已覆盖）。
    /// </summary>
    [Fact]
    public async Task Rebuild_ReportsLedgerMismatch_AndRepairsLedgerFromEventStream()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 2);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var seatOne = new SeatId(1);
        const string originalReason = "test-ledger-original-reason";
        const string marker = "test-ledger-dirty-marker";

        // 行 1：干净流重建 → 步骤机与状态账都应报等价。
        var clean = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "干净流一致性检查",
            "test-ledger-clean");
        Assert.Equal("Accepted", clean.Kind);
        Assert.True(clean.MachineEquivalent);
        Assert.True(clean.LedgerEquivalent);

        var applied = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            seatOne.Value,
            null,
            null,
            null,
            "Drunk",
            null,
            originalReason,
            null,
            "test-ledger-apply");
        Assert.Equal("Accepted", applied.Kind);

        // 只改事件流里的原因文本：步骤机不受影响，但内存账（折过一次）与事件流分叉。
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.Events.SingleAsync(
                item => item.Type == nameof(SeatStateChangedEvent) && item.Payload.Contains(originalReason));
            row.Payload = row.Payload.Replace(originalReason, marker, StringComparison.Ordinal);
            await db.SaveChangesAsync();
        }

        // 行 2：分叉必须被报告，重建把派生数据修回与事件一致。
        var rebuilt = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "账与事件流一致性检查",
            "test-ledger-dirty");
        Assert.Equal("Accepted", rebuilt.Kind);
        Assert.True(rebuilt.MachineEquivalent); // 只改原因文本，步骤机不受影响
        Assert.False(rebuilt.LedgerEquivalent); // 内存账仍是旧原因 → 必须报不一致

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var entry = Assert.Single(view.Seats, seat => seat.Seat == seatOne.Value);
        Assert.Contains(entry.Facts, fact => fact.Reason == marker);
    }

    /// <summary>
    /// 健康票行 1–4：正常房间无降级；恢复失败 → 降级 + 原因；重建失败 → 保持降级、原因更新；
    /// 事件流修复后显式重建成功 → 降级清除。行 5（玩家不下发）在健康态的重连包 wire 上断言。
    /// </summary>
    [Fact]
    public async Task RoomHealth_DegradesOnRestoreFailure_AndClearsOnSuccessfulRebuild()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-health-{Guid.NewGuid():N}.db");
        try
        {
            string? originalPayload = null;

            await using (var first = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 2,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false))
            {
                // 行 1：正常新局（还没观测）健康位正常。
                var healthy = first.Session.GetStorytellerView().Health;
                Assert.False(healthy.IsDegraded);
                Assert.Null(healthy.Reason);

                // 行 5：玩家重连包的 wire 形状里根本没有健康位——隔离靠"不下发"，不靠前端不显示。
                await using var seat = await first.ConnectSeatAsync(new SeatId(1));
                var bundleJson = JsonSerializer.Serialize(
                    first.Bundles[new SeatId(1)],
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.False(bundleJson.Contains("health", StringComparison.OrdinalIgnoreCase), bundleJson);
                Assert.False(bundleJson.Contains("degraded", StringComparison.OrdinalIgnoreCase), bundleJson);

                // 造一条真实的状态账事件：恢复失败后内存账被清空、重建结果非空，
                // "账是否一致"才有可判别的对象（空账对空账会假等价）。
                await using (var seed = await first.ConnectStorytellerAsync())
                {
                    var seeded = await seed.InvokeAsync<CommandResultDto>(
                        "ReportSeatState",
                        new SeatId(1).Value,
                        null,
                        null,
                        null,
                        "Drunk",
                        null,
                        "test-health-seed-ledger",
                        null,
                        "test-health-seed-ledger-key");
                    Assert.Equal("Accepted", seeded.Kind);
                }

                // 弄坏首发事件载荷：重启恢复必然失败（不静默继续）。
                var dbFactory = first.Services.GetRequiredService<IDbContextFactory<GameDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var row = await db.Events.OrderBy(item => item.Sequence).FirstAsync();
                originalPayload = row.Payload;
                row.Payload = "{ this is not valid json";
                await db.SaveChangesAsync();
            }

            await using var revived = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 2,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false);

            // 行 2：恢复失败 → 降级 + 原因（说书人第一次 Join 就能看见）。
            var degraded = revived.Session.GetStorytellerView().Health;
            Assert.True(degraded.IsDegraded);
            Assert.Contains("恢复失败", degraded.Reason ?? string.Empty);
            Assert.Contains("事件载荷损坏", degraded.Reason ?? string.Empty);
            Assert.NotNull(degraded.Since);

            await using var storyteller = await revived.ConnectStorytellerAsync();
            var joinView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.True(joinView.Health.Degraded);
            Assert.Contains("恢复失败", joinView.Health.Reason ?? string.Empty);

            // 玩家侧现状：房间不可读时加入**显式失败**，且错误信息中性——"数据丢了"只说书人可见。
            var setup = await revived.GetSetupAsync();
            var seatTicket = setup.Seats.Single(item => item.Seat == new SeatId(1)).Ticket;
            var raw = await revived.ConnectAnonymousAsync();
            var joinFailure = await Assert.ThrowsAsync<HubException>(() =>
                raw.InvokeAsync<SeatJoinDto>("JoinSeat", seatTicket, 0L));
            Assert.Contains("加入暂时失败", joinFailure.Message);
            Assert.False(joinFailure.Message.Contains("事件载荷", StringComparison.Ordinal));
            Assert.False(joinFailure.Message.Contains("降级", StringComparison.Ordinal));

            // 行 4：重建仍失败 → 降级保持、原因更新为"重建失败"，首次发生时间保留。
            var failed = await storyteller.InvokeAsync<CommandResultDto>(
                "RebuildRoom",
                "事件载荷损坏",
                "test-health-rebuild-1");
            Assert.Equal("Failed", failed.Kind);
            Assert.Null(failed.LedgerEquivalent);

            var stillDegraded = revived.Session.GetStorytellerView().Health;
            Assert.True(stillDegraded.IsDegraded);
            Assert.Contains("重建失败", stillDegraded.Reason ?? string.Empty);
            Assert.Equal(degraded.Since, stillDegraded.Since);

            // 行 3：事件流修复后显式重建成功 → 降级清除（房间重新可恢复）。
            // 报告照实说"重建前内存与重建结果不一致"：恢复失败后内存已被清空，这不是缺陷。
            await using (var db = await revived.Services
                .GetRequiredService<IDbContextFactory<GameDbContext>>()
                .CreateDbContextAsync())
            {
                var row = await db.Events.OrderBy(item => item.Sequence).FirstAsync();
                row.Payload = originalPayload!;
                await db.SaveChangesAsync();
            }

            var rebuilt = await storyteller.InvokeAsync<CommandResultDto>(
                "RebuildRoom",
                "事件流已修复",
                "test-health-rebuild-2");
            Assert.Equal("Accepted", rebuilt.Kind);
            Assert.False(rebuilt.MachineEquivalent);
            Assert.False(rebuilt.LedgerEquivalent);

            var cleared = revived.Session.GetStorytellerView().Health;
            Assert.False(cleared.IsDegraded);
            Assert.Null(cleared.Reason);
            Assert.Null(cleared.Since);
        }
        finally
        {
            DeleteFiles(databasePath);
        }
    }

    /// <summary>
    /// 健康票的另一条路径：宿主一直健康，运行期事件流被改坏 → 只说"重建失败"也必须把健康位置为降级
    /// （原因由 Reason 承载，不能假定总是"恢复失败"），且失败回执必须把更新后的视图推送出去。
    /// </summary>
    [Fact]
    public async Task RebuildFailure_OnHealthyRoom_StillDegradesWithRebuildReason()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 2);
        StorytellerViewDto? pushed = null;
        await using var storyteller = await host.ConnectStorytellerAsync(view => pushed = view);
        Assert.False(host.Session.GetStorytellerView().Health.IsDegraded);

        // 运行期直接改坏一条事件载荷：宿主内存仍健康，但事件流已经不可重建。
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<GameDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.Events.OrderBy(item => item.Sequence).FirstAsync();
            row.Payload = "{ this is not valid json";
            await db.SaveChangesAsync();
        }

        var failed = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "运行期数据异常",
            "test-rebuild-failure-only");
        Assert.Equal("Failed", failed.Kind);
        Assert.Null(failed.LedgerEquivalent);

        var health = host.Session.GetStorytellerView().Health;
        Assert.True(health.IsDegraded);
        Assert.Contains("重建失败", health.Reason ?? string.Empty);
        Assert.NotNull(health.Since);

        // 失败也改了视图（原因更新）→ 必须推送给说书人，否则界面停在旧原因上（对抗复核 2026-10-02）。
        Assert.NotNull(pushed);
        Assert.True(pushed!.Health.Degraded);
        Assert.Contains("重建失败", pushed.Health.Reason ?? string.Empty);
    }

    private static async Task CompleteTestNightAsync(GameClient storyteller)
    {
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var guard = 0;
        while (!view.PlanCompleted && guard++ < 10)
        {
            var result = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：结束测试夜",
                $"test-clear-night-{guard}");
            Assert.Equal("Accepted", result.Kind);
            view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        }

        Assert.True(view.PlanCompleted, "启动测试夜应被强推走完");
    }

    private static void DeleteFiles(string databasePath)
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
