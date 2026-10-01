using System.Collections.Concurrent;
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

    private static async Task CompleteTestNightAsync(HubConnection storyteller)
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
