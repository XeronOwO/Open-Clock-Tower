using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 玩家端视图新鲜度的真宿主行为：作废 / 代填 / 阶段变化必须**在线送达**，
/// 且定向通知（作废 / 代填）不得打扰无关玩家（D-0013 §5）。
/// </summary>
/// <remarks>
/// 跑真宿主 + 真 SignalR + 真 SQLite；界面呈现由验收批次 tools/verify-storyteller-panel.mjs 判。
/// 这里证明的是服务端推送面：消息真的发出、真的只发给该收到的人。
/// </remarks>
public sealed class PlayerPushFreshnessHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>说书人强制作废：当事玩家收到原因与说明，其他玩家零消息。</summary>
    [Fact]
    public async Task ForceVoid_ReachesAddresseeOnly()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        OperationRequestDto? request = null;
        var addresseeVoids = new ConcurrentQueue<OperationRequestVoidedDto>();
        var otherVoids = new ConcurrentQueue<OperationRequestVoidedDto>();
        await host.ConnectSeatAsync(
            new SeatId(1),
            onRequest: value => request = value,
            onVoided: addresseeVoids.Enqueue);
        await host.ConnectSeatAsync(new SeatId(2), onVoided: otherVoids.Enqueue);
        await host.ConnectSeatAsync(new SeatId(3), onVoided: otherVoids.Enqueue);

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount: 3) },
            "test-push-void-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => request is not null, Wait),
            "1 号没有收到操作请求");

        var voided = await storyteller.InvokeAsync<CommandResultDto>(
            "VoidRequest",
            request!.RequestId,
            "StorytellerForce",
            "测试：强制作废",
            "test-push-void");
        Assert.Equal("Accepted", voided.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => addresseeVoids.Count == 1, Wait),
            "1 号没有收到作废推送");
        Assert.True(addresseeVoids.TryPeek(out var pushed));
        Assert.Equal(request.RequestId, pushed.RequestId);
        Assert.Equal("StorytellerForce", pushed.Reason);
        Assert.Equal("测试：强制作废", pushed.Note);

        // 无关玩家零消息（D-0013 §5）：推送已确认送达当事连接后，留一个采样窗口再断言。
        await Task.Delay(300);
        Assert.Empty(otherVoids);
    }

    /// <summary>说书人代填：当事玩家收到"已响应"（来源 = StorytellerProxy），其他玩家零消息。</summary>
    [Fact]
    public async Task ProxyFill_ReachesAddresseeOnly_WithStorytellerSource()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        OperationRequestDto? request = null;
        var addresseeAnswers = new ConcurrentQueue<OperationRequestAnsweredDto>();
        var otherAnswers = new ConcurrentQueue<OperationRequestAnsweredDto>();
        await host.ConnectSeatAsync(
            new SeatId(1),
            onRequest: value => request = value,
            onAnswered: addresseeAnswers.Enqueue);
        await host.ConnectSeatAsync(new SeatId(2), onAnswered: otherAnswers.Enqueue);
        await host.ConnectSeatAsync(new SeatId(3), onAnswered: otherAnswers.Enqueue);

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount: 3) },
            "test-push-proxy-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => request is not null, Wait),
            "1 号没有收到操作请求");

        var filled = await storyteller.InvokeAsync<CommandResultDto>(
            "ProxyFill",
            request!.RequestId,
            "seat:2",
            "测试：说书人代填",
            "test-push-proxy");
        Assert.Equal("Accepted", filled.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => addresseeAnswers.Count == 1, Wait),
            "1 号没有收到代填推送");
        Assert.True(addresseeAnswers.TryPeek(out var pushed));
        Assert.Equal(request.RequestId, pushed.RequestId);
        Assert.Equal("seat:2", pushed.OptionValue);
        Assert.Equal("StorytellerProxy", pushed.Source);
        Assert.Equal("测试：说书人代填", pushed.Note);

        await Task.Delay(300);
        Assert.Empty(otherAnswers);
    }

    /// <summary>阶段开始：全部已绑定席位都收到广播，推送内容就是服务端的阶段名。</summary>
    [Fact]
    public async Task PhaseStart_BroadcastsToEverySeatedPlayer()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            autoStartTestNight: false);

        var seat1Phases = new ConcurrentQueue<PhaseStartedDto>();
        var seat2Phases = new ConcurrentQueue<PhaseStartedDto>();
        var seat3Phases = new ConcurrentQueue<PhaseStartedDto>();
        await host.ConnectSeatAsync(new SeatId(1), onPhaseStarted: seat1Phases.Enqueue);
        await host.ConnectSeatAsync(new SeatId(2), onPhaseStarted: seat2Phases.Enqueue);
        await host.ConnectSeatAsync(new SeatId(3), onPhaseStarted: seat3Phases.Enqueue);

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = TestNightPlan.CreateFirstNight(seatCount: 3) },
            "test-push-phase-start",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => seat1Phases.Count == 1 && seat2Phases.Count == 1 && seat3Phases.Count == 1,
                Wait),
            "并非所有已绑定席位都收到阶段开始广播");
        foreach (var queue in new[] { seat1Phases, seat2Phases, seat3Phases })
        {
            Assert.True(queue.TryPeek(out var pushed));
            Assert.Equal("FirstNight", pushed.Phase);
        }
    }
}
