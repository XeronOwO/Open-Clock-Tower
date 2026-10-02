using System.Collections.Concurrent;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 结束批次的挂起请求作废（票据 ended-game-pending-request-void；D-0010 / D-0014 / R-0024）：
/// 游戏结束时同批产出作废事实、作废排在结束事件之前，快照与重连包里不再留死信。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite。三条链路分别对应矩阵的三种入口：
/// ① 先判即结束（夹具夜晚挂起一条槽位请求，再上报最后一名恶魔死亡）；
/// ② 触发后复判才结束（女巫咒杀被诅咒的最后一名恶魔——死亡由触发管线产出，只有②看得到）；
/// ③ 触发型（呆瓜）请求在结束态同样被作废。
/// </remarks>
public sealed class EndedGamePendingRequestVoidTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>① 先判即结束 + 挂起槽位请求：同批作废、快照无挂起、重连无死信、重启可重放、日志留痕。</summary>
    [Fact]
    public async Task FirstEvaluationEnd_VoidsPendingSlotRequest_InTheEndingBatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-ending-void-{Guid.NewGuid():N}.db");
        try
        {
            await RunFirstEvaluationEndAsync(databasePath);
        }
        finally
        {
            // 失败路径也要清场（重启段可能在失败时还没跑到）：与 StepMachineHostTests 同款收尾。
            DeleteFiles(databasePath);
        }
    }

    /// <summary>① 的场景本体；拆出方法是为了让整段（含失败路径）共用一次清场收尾。</summary>
    private static async Task RunFirstEvaluationEndAsync(string databasePath)
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
                    new() { Seat = 1, Character = "klutz" },
                    new() { Seat = 2, Character = "vortox" },
                    new() { Seat = 3, Character = "clockmaker" },
                },
                "test-ending-assign");
            Assert.Equal("Accepted", assigned.Kind);

            var requests = new ConcurrentQueue<OperationRequestDto>();
            var voids = new ConcurrentQueue<OperationRequestVoidedDto>();
            await using var seatOne = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue, voids.Enqueue);

            // 夹具计划（TestNightPlan）只为让 1 号槽位开出一条挂起的槽位请求；它不是规则数据。
            var started = await host.ExecuteHostCommandAsync(
                new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(3) },
                "test-ending-start",
                CancellationToken.None);
            Assert.Equal(CommandResultKind.Accepted, started.Kind);
            Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait), "夹具槽位没有开出请求");
            var request = requests.First();
            Assert.Equal(1, request.Seat);

            // 系统专属原因不接受手动使用：说书人手动传 GameEnded 必须被合法性闸拒绝（枚举登记 ≠ 可选），
            // 拒绝后请求仍然挂起（闸不改状态）。
            var forbidden = await storyteller.InvokeAsync<CommandResultDto>(
                "VoidRequest",
                request.RequestId,
                "GameEnded",
                "测试：系统专属原因不接受手动选择",
                "test-ending-forbidden");
            Assert.Equal("Rejected", forbidden.Kind);
            Assert.Equal("legality.reason_invalid", forbidden.RejectionCode);
            Assert.NotNull(host.Session.GetPlayerView(new SeatId(1)).PendingRequest);

            // 上报唯一恶魔（2 号）死亡：① 先判当场结束——挂起请求必须同批作废。
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
                "test-ending-death");
            Assert.Equal("Accepted", death.Kind);

            // 玩家收到"请求被作废"推送：死信不会以"还能答"的形态留在客户端。
            Assert.True(await TestServerHost.WaitUntilAsync(() => !voids.IsEmpty, Wait), "玩家没有收到作废推送");
            Assert.Equal(request.RequestId, voids.First().RequestId);
            Assert.Equal("GameEnded", voids.First().Reason);
            Assert.Contains("本局已结束", Assert.IsType<string>(voids.First().Note), StringComparison.Ordinal);

            var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            var voidStored = Assert.Single(
                stored,
                item => item.Event is OperationRequestVoidedEvent { Void.Reason: OperationRequestVoidReason.GameEnded });
            var endStored = Assert.Single(stored, item => item.Event is GameEndedEvent);
            var voided = Assert.IsType<OperationRequestVoidedEvent>(voidStored.Event);
            Assert.Equal(request.RequestId, voided.RequestId.Value);
            Assert.True(voidStored.Sequence < endStored.Sequence, "作废事件必须排在 GameEndedEvent 之前");

            // 快照：挂起被折成"已作废"，终局不再持有等待中的请求。
            var snapshot = await host.Store.FindSnapshotAsync(TestServerHost.GameId, CancellationToken.None);
            Assert.NotNull(snapshot);
            var machine = snapshot!.Machine!;
            var pending = machine.PendingRequest;
            Assert.NotNull(pending);
            Assert.Equal(OperationRequestStatus.Voided, pending!.Status);
            Assert.Equal(OperationRequestVoidReason.GameEnded, pending.Voided!.Reason);
            Assert.False(machine.IsHeld);

            // 投影与说书人视图：玩家拿不到请求；说书人看到的是"因结束而作废"，不是静默消失。
            Assert.Null(host.Session.GetPlayerView(new SeatId(1)).PendingRequest);
            Assert.NotNull(host.Session.GetPlayerView(new SeatId(1)).Outcome);
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            Assert.Null(view.Pending);
            Assert.Equal("GameEnded", view.LastVoidedRequest!.Reason);

            // 重连包：快照里没有请求，补齐事件里有一条"请求被作废"（D-0010 / D-0012）。
            var bundle = await host.Session.GetReconnectBundleAsync(new SeatId(1), 0, CancellationToken.None);
            Assert.Null(bundle.View.PendingRequest);
            Assert.Contains(bundle.EventsSince, item => item.Kind == PlayerEventKind.RequestVoided);

            // 矩阵行 6：作废日志带游戏局、请求标识与原因。
            Assert.Contains(host.Logs, line =>
                line.Contains("结束批次作废挂起请求", StringComparison.Ordinal)
                && line.Contains(request.RequestId, StringComparison.Ordinal)
                && line.Contains("GameEnded", StringComparison.Ordinal));
        }

        // 矩阵行 4/5：同库重启 = 只折事件重放；终局仍不带死信，结论同源。
        await using (var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true,
            autoStartTestNight: false))
        {
            Assert.True(
                await TestServerHost.WaitUntilAsync(
                    () => restarted.Session.GetPlayerView(new SeatId(1)).Outcome is not null,
                    Wait),
                "重启后没有从事件流恢复出终局结论");

            var playerView = restarted.Session.GetPlayerView(new SeatId(1));
            Assert.Null(playerView.PendingRequest);
            Assert.Equal(Alignment.Good, playerView.Outcome!.Winner);

            var storytellerView = restarted.Session.GetStorytellerView();
            Assert.Null(storytellerView.Pending);
            Assert.NotNull(storytellerView.Outcome);
        }
    }

    /// <summary>
    /// ② 触发后复判才结束：业务事件（提名）只让账"还没结束"，触发管线产出的死亡才让恶魔全死成立；
    /// 本批没有挂起请求，因此也不产生多余作废事件。
    /// </summary>
    [Fact]
    public async Task SecondEvaluationEnd_ClosesTheBatchThroughTheSameEndingPath()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "witch" },
                new() { Seat = 2, Character = "no-dashii" },
                new() { Seat = 3, Character = "clockmaker" },
                new() { Seat = 4, Character = "dreamer" },
                new() { Seat = 5, Character = "klutz" },
            },
            "test-ending-second-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var witchRequests = new ConcurrentQueue<OperationRequestDto>();
        await using var witch = await host.ConnectSeatAsync(new SeatId(1), witchRequests.Enqueue);
        await using var cursedDemon = await host.ConnectSeatAsync(new SeatId(2));

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-ending-second-night");
        Assert.Equal("Accepted", night.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !witchRequests.IsEmpty, Wait), "女巫没有收到操作请求");
        var curse = witchRequests.First();
        Assert.Equal(1, curse.Seat);
        Assert.Equal(
            "Accepted",
            (await witch.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                curse.RequestId,
                "seat:2",
                "test-ending-second-curse",
                1L)).Kind);
        await CompleteNightAsync(storyteller, "second");

        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-ending-second-day");
        Assert.Equal("Accepted", day.Kind);

        // 被诅咒的恶魔发起提名：① 只看到提名（恶魔还活着）→ 触发管线咒杀 → ② 恶魔全死、善良获胜。
        var nominated = await cursedDemon.InvokeAsync<CommandResultDto>(
            "Nominate",
            1,
            "test-ending-second-nominate");
        Assert.Equal("Accepted", nominated.Kind);

        var ended = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Outcome is not null,
            Wait);
        Assert.NotNull(ended);
        Assert.Equal("Good", ended!.Outcome!.Winner);
        Assert.Equal("DemonsAllDead", ended.Outcome.Condition);

        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        var nominationSequence = Assert.Single(
            stored,
            item => item.Event is NominationMadeEvent { Nominator.Value: 2 }).Sequence;
        var deathSequence = Assert.Single(
            stored,
            item => item.Event is SeatStateChangedEvent { Seat.Value: 2, Life: LifeState.Dead }).Sequence;
        var endSequence = Assert.Single(stored, item => item.Event is GameEndedEvent).Sequence;

        // 死亡由触发管线产出（紧跟业务事件），结束事件排在它之后：本批确实走的是②复判。
        Assert.Equal(nominationSequence + 1, deathSequence);
        Assert.True(endSequence > deathSequence, "结束事件必须在触发产出的死亡之后");
        Assert.Equal(stored[^1].Sequence, endSequence);

        // 本批没有挂起请求可作废：不产生多余作废事件。
        Assert.DoesNotContain(
            stored,
            item => item.Event is OperationRequestVoidedEvent && item.Sequence >= nominationSequence);

        var snapshot = await host.Store.FindSnapshotAsync(TestServerHost.GameId, CancellationToken.None);
        Assert.NotNull(snapshot);
        var machine = snapshot!.Machine!;
        Assert.NotNull(machine.Outcome);
        Assert.Null(machine.PendingRequest);
    }

    /// <summary>
    /// ③ 触发型（呆瓜）请求在结束态被作废：结束批次不跑触发管线（《处决》第 3 步），
    /// 因此不再补"呆瓜跳过"记录——作废事实由那条作废事件承载（口径见票据「边界与同类检查」）。
    /// </summary>
    [Fact]
    public async Task TriggerRequestPendingAtEnd_IsVoided_WhenTheEndComesFromAnotherDeath()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "klutz" },
                new() { Seat = 2, Character = "no-dashii" },
                new() { Seat = 3, Character = "clockmaker" },
                new() { Seat = 4, Character = "dreamer" },
                new() { Seat = 5, Character = "witch" },
            },
            "test-ending-trigger-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var klutzRequests = new ConcurrentQueue<OperationRequestDto>();
        await using var klutz = await host.ConnectSeatAsync(new SeatId(1), klutzRequests.Enqueue);

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-ending-trigger-night");
        Assert.Equal("Accepted", night.Kind);
        await CompleteNightAsync(storyteller, "trigger");

        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-ending-trigger-day");
        Assert.Equal("Accepted", day.Kind);

        // 呆瓜白天死亡：即时公告开出**触发来源**的公开选择请求（R-0027）。
        var klutzDeath = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：呆瓜白天死亡",
            null,
            "test-ending-trigger-klutz");
        Assert.Equal("Accepted", klutzDeath.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !klutzRequests.IsEmpty, Wait), "呆瓜没有收到死亡选择请求");
        var klutzRequest = klutzRequests.First();
        Assert.Equal("klutz:1", klutzRequest.RequestId);

        // 上报最后一名恶魔死亡：① 先判结束；仍挂起的触发型请求同批作废。
        var demonDeath = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            2,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：上报最后一名恶魔死亡",
            null,
            "test-ending-trigger-demon");
        Assert.Equal("Accepted", demonDeath.Kind);

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var voidStored = Assert.Single(
            stored,
            item => item.Event is OperationRequestVoidedEvent { Void.Reason: OperationRequestVoidReason.GameEnded });
        var endStored = Assert.Single(stored, item => item.Event is GameEndedEvent);
        var voided = Assert.IsType<OperationRequestVoidedEvent>(voidStored.Event);
        Assert.Equal("klutz:1", voided.RequestId.Value);
        Assert.True(voidStored.Sequence < endStored.Sequence);
        Assert.DoesNotContain(stored, item => item.Event is KlutzChoiceSkippedEvent);

        Assert.Null(host.Session.GetPlayerView(new SeatId(1)).PendingRequest);
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(view.Pending);
        Assert.Equal("GameEnded", view.LastVoidedRequest!.Reason);
    }

    /// <summary>
    /// ④ 与挂起请求同族的第二个挂起：等待说书人的裁定点。结束批次同样把它收口——
    /// 终局快照不留再也答不了的「等待裁定」（一切输入都被 `phase.game_ended` 拒）。
    /// </summary>
    [Fact]
    public async Task PendingDecisionPointAtEnd_IsResolvedInTheEndingBatch()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "klutz" },
                new() { Seat = 2, Character = "vortox" },
                new() { Seat = 3, Character = "clockmaker" },
            },
            "test-ending-decision-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 夹具裁定槽位：进入即挂一个「由说书人决定」的裁定点（不是操作请求）。
        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateDecisionFirstNight(3) },
            "test-ending-decision-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        var awaiting = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(awaiting.AwaitingDecisionId);

        // 上报唯一恶魔死亡：① 先判结束；挂起的裁定点必须在结束事件之前收口。
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
            "test-ending-decision-death");
        Assert.Equal("Accepted", death.Kind);

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var raisedStored = Assert.Single(stored, item => item.Event is DecisionPointRaisedEvent);
        var resolvedStored = Assert.Single(stored, item => item.Event is DecisionPointResolvedEvent);
        var raised = Assert.IsType<DecisionPointRaisedEvent>(raisedStored.Event);
        var resolved = Assert.IsType<DecisionPointResolvedEvent>(resolvedStored.Event);
        var endStored = Assert.Single(stored, item => item.Event is GameEndedEvent);
        Assert.Equal(raised.DecisionPoint.Id, resolved.DecisionPointId);
        Assert.Null(resolved.Decision);
        Assert.Contains("本局已结束", Assert.IsType<string>(resolved.Note), StringComparison.Ordinal);
        Assert.True(resolvedStored.Sequence < endStored.Sequence, "裁定点收口必须排在 GameEndedEvent 之前");

        // 快照与说书人视图：终局不再持有悬挂的裁定点。
        var snapshot = await host.Store.FindSnapshotAsync(TestServerHost.GameId, CancellationToken.None);
        Assert.NotNull(snapshot);
        var machine = snapshot!.Machine!;
        Assert.Null(machine.AwaitingDecision);
        Assert.False(machine.IsHeld);

        var after = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(after.AwaitingDecisionId);
        Assert.NotNull(after.Outcome);
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：这些用例只真正结算与角色有关的那一步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-ending-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                // 计划在「查视图」与「强推」之间被自动推进走完（0.05s 配额档下的固有竞态）：
                // 目标已经达成，不算失败。
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 24 次强推内没有走完");
    }

    /// <summary>删掉测试库文件（含 WAL / SHM）；失败路径与正常路径共用。</summary>
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
