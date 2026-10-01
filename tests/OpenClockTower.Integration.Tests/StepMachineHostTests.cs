using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 真实宿主上的操作请求链路：单播、重连重投、无超时、卡点、作废 / 代填、四道闸、幂等
/// （验收矩阵行 1–11）。
/// </summary>
public sealed class StepMachineHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(6);

    /// <summary>行 1 / 17：请求只推给当事玩家，其他玩家的设备没有任何活动指示。</summary>
    [Fact]
    public async Task Row1_NightSlot_PushesRequestOnlyToActor()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var seat1 = new ConcurrentQueue<OperationRequestDto>();
        var seat2 = new ConcurrentQueue<OperationRequestDto>();
        var seat3 = new ConcurrentQueue<OperationRequestDto>();
        var seat2Views = new ConcurrentQueue<StorytellerViewDto>();
        var seat2Voids = new ConcurrentQueue<OperationRequestVoidedDto>();

        await using var connection1 = await host.ConnectSeatAsync(new SeatId(1), seat1.Enqueue);
        await using var connection2 = await host.ConnectSeatAsync(
            new SeatId(2),
            seat2.Enqueue,
            seat2Voids.Enqueue,
            onStorytellerView: seat2Views.Enqueue);
        await using var connection3 = await host.ConnectSeatAsync(new SeatId(3), seat3.Enqueue);

        Assert.True(await TestServerHost.WaitUntilAsync(() => !seat1.IsEmpty, Wait), "1 号应收到定向操作请求");
        await Task.Delay(300);

        Assert.Empty(seat2);
        Assert.Empty(seat3);
        Assert.Empty(seat2Views);
        Assert.Empty(seat2Voids);
        var request = seat1.First();
        Assert.Equal(1, request.Seat);
        Assert.Equal("demo:night-1:demo-seat-1", request.RequestId);
        Assert.NotEmpty(request.Options);
    }

    /// <summary>行 2：断线重连后**重新收到同一个未响应请求**，内容一致。</summary>
    [Fact]
    public async Task Row2_Reconnect_RedeliversIdenticalRequest()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var first = new ConcurrentQueue<OperationRequestDto>();
        var connection = await host.ConnectSeatAsync(new SeatId(1), first.Enqueue);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !first.IsEmpty, Wait));
        var original = first.First();
        await connection.DisposeAsync();

        var second = new ConcurrentQueue<OperationRequestDto>();
        await using var reconnected = await host.ConnectSeatAsync(new SeatId(1), second.Enqueue);

        Assert.True(await TestServerHost.WaitUntilAsync(() => !second.IsEmpty, Wait), "重连后必须重投未响应请求");
        var redelivered = second.First();
        Assert.Equal(original.RequestId, redelivered.RequestId);
        Assert.Equal(original.Seat, redelivered.Seat);
        Assert.Equal(original.Context, redelivered.Context);
        Assert.Equal(
            original.Options.Select(option => option.Value),
            redelivered.Options.Select(option => option.Value));

        var bundle = host.Bundles[new SeatId(1)];
        Assert.Equal(original.RequestId, bundle.View.PendingRequest?.RequestId);
    }

    /// <summary>行 3：玩家长时间不响应 → 请求一直有效，不自动过期、不自动跳过。</summary>
    [Fact]
    public async Task Row3_NoResponse_RequestStaysPendingAndSlotDoesNotAdvance()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.1, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));
        var requestId = requests.First().RequestId;

        await Task.Delay(700);
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");

        Assert.Equal(0, view.SlotIndex);
        Assert.NotNull(view.Pending);
        Assert.Equal(requestId, view.Pending!.RequestId);
    }

    /// <summary>行 4：说书人能看到"谁在卡着、卡在哪一步、卡了多久"。</summary>
    [Fact]
    public async Task Row4_StorytellerSeesWhoIsStuckAndWhere()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        await Task.Delay(700);
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");

        Assert.NotNull(view.Pending);
        Assert.Equal(1, view.Pending!.Seat);
        Assert.Equal("demo-seat-1", view.Pending.SlotId);
        Assert.Equal(0, view.Pending.SlotIndex);
        Assert.NotNull(view.Pending.WaitingSeconds);
        Assert.True(view.Pending.WaitingSeconds > 0.5, "等待时长应随真实时间增长");
    }

    /// <summary>行 5：说书人强制作废 → 原因留痕；配额到点后按细则继续。</summary>
    [Fact]
    public async Task Row5_StorytellerForceVoid_RecordedAndContinuesAfterQuota()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.1, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "VoidRequest",
            requests.First().RequestId,
            "StorytellerForce",
            "玩家确认无法操作",
            "test-void-1");

        Assert.Equal("Accepted", result.Kind);
        var view = await TestServerHost.WaitForViewAsync(storyteller, v => v.Pending is null && v.SlotIndex >= 1);
        Assert.NotNull(view);
        Assert.True(view!.SlotIndex >= 1, "配额到点且请求已了结后应继续推进");

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var voided = stored.Select(item => item.Event).OfType<OperationRequestVoidedEvent>().Single();
        Assert.Equal(OperationRequestVoidReason.StorytellerForce, voided.Void.Reason);
        Assert.Equal("玩家确认无法操作", voided.Void.Note);
    }

    /// <summary>行 6：说书人代填 → 事件里标明"由说书人代填"。</summary>
    [Fact]
    public async Task Row6_StorytellerProxyFill_MarkedAsProxy()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "ProxyFill",
            requests.First().RequestId,
            "seat:2",
            "玩家掉线，说书人代填",
            "test-proxy-1");

        Assert.Equal("Accepted", result.Kind);
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var answered = stored.Select(item => item.Event).OfType<OperationRequestAnsweredEvent>().Single();
        Assert.Equal(ResponseSource.StorytellerProxy, answered.Answer.Source);
        Assert.Equal("seat:2", answered.Answer.OptionValue);
        Assert.Equal("玩家掉线，说书人代填", answered.Answer.Note);
    }

    /// <summary>行 7：请求挂起期间服务端重启 → 票据仍有效，重连即可拿回同一请求。</summary>
    [Fact]
    public async Task Row7_HostRestart_KeepsPendingRequestAndTicket()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-restart-{Guid.NewGuid():N}.db");
        string originalRequestId;
        long firstLastSequence;
        try
        {
            var first = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 3,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false);
            var firstRequests = new ConcurrentQueue<OperationRequestDto>();
            var connection = await first.ConnectSeatAsync(new SeatId(1), firstRequests.Enqueue);
            Assert.True(await TestServerHost.WaitUntilAsync(() => !firstRequests.IsEmpty, Wait));
            originalRequestId = firstRequests.First().RequestId;

            var firstEvents = await first.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            firstLastSequence = firstEvents[^1].Sequence;
            Assert.Single(firstEvents.Select(item => item.Event).OfType<PhaseStartedEvent>());

            await connection.DisposeAsync();
            await first.DisposeAsync();

            await using var second = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 3,
                databasePath: databasePath);
            var secondRequests = new ConcurrentQueue<OperationRequestDto>();
            await using var reconnected = await second.ConnectSeatAsync(new SeatId(1), secondRequests.Enqueue);

            Assert.True(
                await TestServerHost.WaitUntilAsync(() => !secondRequests.IsEmpty, Wait),
                "重启后重连应重投同一请求");
            Assert.Equal(originalRequestId, secondRequests.First().RequestId);

            // 反证：重启必须走"恢复事件流"，而不是把库当成新局重新播种
            // （请求 ID 是确定性的，只比较 ID 的话"重新开局"这条错误路径也会碰巧通过）
            var secondEvents = await second.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            Assert.Equal(firstLastSequence, secondEvents[^1].Sequence);
            Assert.Single(secondEvents.Select(item => item.Event).OfType<PhaseStartedEvent>());
            var bundle = second.Bundles[new SeatId(1)];
            Assert.Equal(firstLastSequence, bundle.Sequence);
            Assert.Contains(bundle.Events, playerEvent => playerEvent.Kind == "PhaseStarted");
        }
        finally
        {
            DeleteFiles(databasePath);
        }
    }

    /// <summary>行 8：座位依赖失效（死亡 / 角色变更）→ 自动作废并推送给当事玩家；说书人看到归因。</summary>
    [Fact]
    public async Task Row8_SeatDependencyLost_AutoVoidsPendingRequest()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        var voided = new ConcurrentQueue<OperationRequestVoidedDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue, voided.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            "clockmaker",
            null,
            null,
            null,
            "测试：1 号被投毒致死",
            2,
            "test-seatchange-1");

        Assert.Equal("Accepted", result.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !voided.IsEmpty, Wait), "作废应推送给当事玩家");
        Assert.Equal("DependencyViolated", voided.First().Reason);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(view.Pending);
        var change = Assert.Single(view.RecentSeatChanges);
        Assert.Equal(1, change.Seat);
        Assert.Equal("Dead", change.Life);
        Assert.Equal("测试：1 号被投毒致死", change.Reason);
        Assert.Equal(2, change.CausedBy);
    }

    /// <summary>行 8 的另一半：只改角色（不动生死）也必须自动作废，且不得编造未观测的维度。</summary>
    [Fact]
    public async Task Row8b_CharacterChangeOnly_AutoVoidsPendingRequest()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        var voided = new ConcurrentQueue<OperationRequestVoidedDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue, voided.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            "soldier",
            null,
            null,
            null,
            "测试：1 号角色被交换",
            null,
            "test-seatchar-1");

        Assert.Equal("Accepted", result.Kind);
        Assert.True(await TestServerHost.WaitUntilAsync(() => !voided.IsEmpty, Wait), "只改角色也必须作废");
        Assert.Equal("DependencyViolated", voided.First().Reason);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(view.Pending);
        var change = Assert.Single(view.RecentSeatChanges);
        Assert.Null(change.Life);
        Assert.Equal("soldier", change.Character);
    }

    /// <summary>行 16 的协议级反方向验证：重连补齐不下发他人请求、槽位与计划。</summary>
    [Fact]
    public async Task Row16_ReconnectBundle_IsPlayerScoped()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var seat1Requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), seat1Requests.Enqueue);
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        Assert.True(await TestServerHost.WaitUntilAsync(() => !seat1Requests.IsEmpty, Wait));
        var otherRequestId = seat1Requests.First().RequestId;

        var seat2Bundle = host.Bundles[new SeatId(2)];
        Assert.Null(seat2Bundle.View.PendingRequest);
        Assert.DoesNotContain(seat2Bundle.Events, playerEvent => playerEvent.Kind == "RequestIssued");
        var seat2Json = JsonSerializer.Serialize(seat2Bundle);
        Assert.DoesNotContain(otherRequestId, seat2Json, StringComparison.Ordinal);
        Assert.DoesNotContain("slot", seat2Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planlabel", seat2Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo:night-1", seat2Json, StringComparison.Ordinal);

        var seat1Bundle = host.Bundles[new SeatId(1)];
        Assert.Contains(
            seat1Bundle.Events,
            playerEvent => playerEvent.Kind == "RequestIssued" && playerEvent.Request?.RequestId == otherRequestId);
        var seat1Json = JsonSerializer.Serialize(seat1Bundle);
        Assert.DoesNotContain("slot", seat1Json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planlabel", seat1Json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>行 9：非法选项被合法性闸拒绝，状态不变。</summary>
    [Fact]
    public async Task Row9_IllegalOption_RejectedAndStateUnchanged()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));
        var requestId = requests.First().RequestId;

        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requestId,
            "seat:99",
            "test-illegal-1",
            1L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("legality.option_not_legal", result.RejectionCode);
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal(requestId, view.Pending?.RequestId);
    }

    /// <summary>行 10：非当事玩家提交响应 → 被阶段闸拒绝。</summary>
    [Fact]
    public async Task Row10_NonActorResponse_RejectedByPhaseGate()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await seat2.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requests.First().RequestId,
            "seat:2",
            "test-offturn-1",
            1L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("phase.not_your_request", result.RejectionCode);
    }

    /// <summary>行 11：同一幂等键重复投递 → 只生效一次，第二次返回同一结果。</summary>
    [Fact]
    public async Task Row11_DuplicateResponse_AppliesOnceAndReturnsSameResult()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));
        var requestId = requests.First().RequestId;

        var first = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requestId,
            "seat:2",
            "test-dup-1",
            1L);
        var second = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requestId,
            "seat:2",
            "test-dup-1",
            1L);

        Assert.Equal("Accepted", first.Kind);
        Assert.Equal("Duplicate", second.Kind);
        Assert.Equal(first.Sequence, second.Sequence);

        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        Assert.Single(stored.Select(item => item.Event).OfType<OperationRequestAnsweredEvent>());
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
