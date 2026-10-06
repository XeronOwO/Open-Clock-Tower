using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 终局残留挂起：阻塞报警在结束批次被显式收口（票据 terminal-hold-residue；
/// R-0024 / D-0010 / D-0014）。同批产出"阻塞被解除"的事实、排在结束事件之前，
/// 终局快照不再自称挂起，说书人视图不再留一块点不动的死控件。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite。阻塞夹具走**生产置位路径**
/// （空槽位却绑着一名存活持有者、而这一格没有行动契约 → <see cref="SlotBlockedEvent"/>）；
/// 结束入口用「上报最后一名恶魔死亡」，与票据 ended-game-pending-request-void 同一条收口点
/// （<c>SessionCommit.AppendGameEnding</c>）。
/// </remarks>
public sealed class TerminalHoldResidueTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 矩阵行 1 / 3 / 4 / 5 / 6 / 7：结束批次遇阻塞 → 同批解除、排序正确、只清阻塞、日志留痕、
    /// 重启与重放都能还原同一终局（不带阻塞报警）。
    /// </summary>
    [Fact]
    public async Task BlockedSlotAtEnd_IsUnblocked_InTheEndingBatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-terminal-hold-{Guid.NewGuid():N}.db");
        try
        {
            await RunBlockedEndingAsync(databasePath);
        }
        finally
        {
            // 失败路径也要清场（重启段可能在失败时还没跑到）：与 EndedGamePendingRequestVoidTests 同款收尾。
            DeleteFiles(databasePath);
        }
    }

    /// <summary>行 1 / 3 / 4 的场景本体；拆出方法是为了让整段（含失败路径）共用一次清场收尾。</summary>
    private static async Task RunBlockedEndingAsync(string databasePath)
    {
        await using (var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false,
            autoStartTestNight: false))
        {
            await using var storyteller = await host.ConnectStorytellerAsync();
            var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                new SeatCharacterAssignmentDto[]
                {
                    new() { Seat = 1, Character = "sage" },
                    new() { Seat = 2, Character = "vortox" },
                    new() { Seat = 3, Character = "clockmaker" },
                },
                "test-hold-assign");
            Assert.Equal("Accepted", assigned.Kind);

            // 夹具计划：1 号槽位是空槽位却绑着"贤者"，而 1 号（存活）正是持有者、这一格又没有契约
            // → 进入即阻塞报警（R-0009 BlockAndAlert）。夹具只驱动链路，不是规则数据。
            var started = await host.ExecuteHostCommandAsync(
                new StartPhaseCommand { Plan = TestNightPlan.CreateBlockedFirstNight(3, blockedCharacter: "sage") },
                "test-hold-start",
                CancellationToken.None);
            Assert.Equal(CommandResultKind.Accepted, started.Kind);

            // 结束之前：报警是**活的**——说书人视图上确实挂着一条处理不了的阻塞原因。
            var before = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            var blockedReason = Assert.IsType<string>(before.BlockedReason);
            Assert.Null(before.Outcome);

            var blockedSnapshot = await host.Store.FindSnapshotAsync(TestServerHost.GameId, CancellationToken.None);
            Assert.NotNull(blockedSnapshot);
            Assert.NotNull(blockedSnapshot!.Machine!.Block);
            Assert.True(blockedSnapshot.Machine.IsHeld);

            // 上报唯一恶魔（2 号）死亡：① 先判当场结束——阻塞报警必须同批被解除。
            var death = await storyteller.InvokeAsync<CommandResultDto>(
                "ReportSeatState",
                2,
                "Dead",
                null,
                null,
                null,
                null,
                "测试：上报最后一名恶魔死亡",
                null,
                "test-hold-death");
            Assert.Equal("Accepted", death.Kind);

            var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
            var unblockStored = Assert.Single(stored, item => item.Event is SlotUnblockedEvent);
            var unblocked = Assert.IsType<SlotUnblockedEvent>(unblockStored.Event);
            var endStored = Assert.Single(stored, item => item.Event is GameEndedEvent);

            // 行 1：显式收口（事件 + 原因），且指向被阻塞的那一格。
            Assert.Equal(new StepSlotId("test-seat-1"), unblocked.SlotId);
            Assert.Contains("本局已结束", unblocked.Reason, StringComparison.Ordinal);

            // 行 6：解除事件排在结束事件之前，且结束事件是本批最后一条（收口不产生后续事件）。
            Assert.True(unblockStored.Sequence < endStored.Sequence, "解除阻塞必须排在 GameEndedEvent 之前");
            Assert.Equal(stored[^1].Sequence, endStored.Sequence);

            // 行 4：只清阻塞——位置与最小配额一个都没动，其余字段由内核折叠测试锁定。
            var snapshot = await host.Store.FindSnapshotAsync(TestServerHost.GameId, CancellationToken.None);
            Assert.NotNull(snapshot);
            var machine = snapshot!.Machine!;
            Assert.Null(machine.Block);
            Assert.False(machine.IsHeld);
            Assert.NotNull(machine.Outcome);
            Assert.Equal(blockedSnapshot.Machine.SlotIndex, machine.SlotIndex);
            Assert.Equal(blockedSnapshot.Machine.Quota, machine.Quota);

            // 行 5：说书人视图上那块死控件消失；结束横幅照常。
            var after = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.Null(after.BlockedReason);
            Assert.NotNull(after.Outcome);

            // 行 7：日志带游戏局、槽位、原阻塞原因与说明。
            Assert.Contains(host.Logs, line =>
                line.Contains("结束批次解除阻塞报警", StringComparison.Ordinal)
                && line.Contains("test-seat-1", StringComparison.Ordinal)
                && line.Contains(blockedReason, StringComparison.Ordinal));

            // 行 3 的"重放"面：把整条事件流交给内核重新折一遍（不读快照），终局状态同样不带阻塞报警。
            var replayed = stored.Aggregate(
                (StepMachineState?)null,
                (state, item) => StepMachine.Apply(state, item.Event));
            Assert.NotNull(replayed);
            Assert.Null(replayed!.Block);
            Assert.False(replayed.IsHeld);
            Assert.NotNull(replayed.Outcome);
            Assert.Equal(machine.SlotIndex, replayed.SlotIndex);
            Assert.Equal(machine.Quota, replayed.Quota);
        }

        // 行 3 的"重启"面：同库重启 = 恢复同一终局；玩家与说书人都看到结束、没有任何阻塞报警。
        await using (var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true,
            autoStartTestNight: false))
        {
            Assert.True(
                await TestServerHost.WaitUntilAsync(
                    () => restarted.Session.GetStorytellerView().Outcome is not null,
                    Wait),
                "重启后没有从事件流恢复出终局结论");

            var storytellerView = restarted.Session.GetStorytellerView();
            Assert.Null(storytellerView.BlockedReason);
            Assert.NotNull(storytellerView.Outcome);

            var playerView = restarted.Session.GetPlayerView(new SeatId(1));
            Assert.NotNull(playerView.Outcome);
        }
    }

    /// <summary>
    /// 矩阵行 2：结束批次**没有**阻塞报警时不产生多余事件（幂等）。
    /// 同一个夹具计划的阻塞面被拆掉——计划里绑的角色（dreamer）没分配给任何席位，
    /// <c>OrphanReason</c> 找不到"恰好一名存活持有者"，这一格安静地空着。
    /// </summary>
    [Fact]
    public async Task EndingWithoutBlock_EmitsNoUnblockEvent()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "clockmaker" },
                new() { Seat = 2, Character = "vortox" },
                new() { Seat = 3, Character = "sage" },
            },
            "test-hold-clean-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateBlockedFirstNight(3, blockedCharacter: "dreamer") },
            "test-hold-clean-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);
        Assert.Null((await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView")).BlockedReason);

        var death = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            2,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：上报最后一名恶魔死亡",
            null,
            "test-hold-clean-death");
        Assert.Equal("Accepted", death.Kind);

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        Assert.Single(stored, item => item.Event is GameEndedEvent);
        Assert.DoesNotContain(stored, item => item.Event is SlotUnblockedEvent);
    }

    /// <summary>删掉测试库文件（含 WAL / SHM / 单实例锁）；失败路径与正常路径共用。</summary>
    private static void DeleteFiles(string databasePath) => TestDatabaseFiles.Delete(databasePath);
}
