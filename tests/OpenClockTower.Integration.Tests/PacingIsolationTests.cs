using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 行 12–20：恒定夜晚节奏（时序不泄漏）在真实宿主上的证据。
/// </summary>
public sealed class PacingIsolationTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

    /// <summary>行 13 / 14 / 18：空槽位照样走配额；秒回不提前推进；各槽位节奏一致。</summary>
    [Fact]
    public async Task Slots_ConsumeQuota_AndInstantResponseDoesNotShorten()
    {
        const double quota = 0.2;
        await using var host = new TestServerHost(quota, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requests.First().RequestId,
            "seat:2",
            "test-pace-1",
            1L);
        Assert.Equal("Accepted", result.Kind);

        var view = await TestServerHost.WaitForViewAsync(storyteller, v => v.PlanCompleted, TimeSpan.FromSeconds(10));
        Assert.NotNull(view);
        Assert.True(view!.PlanCompleted, "演示夜应走完（含黎明槽位）");

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var entered = events
            .Where(item => item.Event is SlotEnteredEvent)
            .Select(item => item.RecordedAt)
            .ToArray();
        var completedAt = events
            .Single(item => item.Event is PhaseCompletedEvent { PlanLabel: "demo:night-1" })
            .RecordedAt;

        Assert.Equal(4, entered.Length); // 行动槽位 + 2 个空槽位 + 黎明
        var durations = Durations([.. entered, completedAt]);
        Assert.All(
            durations,
            duration => Assert.True(
                duration.TotalSeconds >= quota * 0.7,
                $"槽位时长 {duration.TotalSeconds:F3}s 短于配额 {quota}s（D-0013 §4）"));
        Assert.True(
            durations.Max() - durations.Min() < TimeSpan.FromMilliseconds(500),
            "各槽位节奏差异过大：配额不随角色是否行动变化（D-0013 §1/§2）");
    }

    /// <summary>行 15：作废不得缩短当前槽位（玩家秒回同理，已在上一用例覆盖）。</summary>
    [Fact]
    public async Task Void_DoesNotShortenSlot()
    {
        const double quota = 0.25;
        await using var host = new TestServerHost(quota, seatCount: 2);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "VoidRequest",
            requests.First().RequestId,
            "StorytellerForce",
            null,
            "test-void-pace-1");
        Assert.Equal("Accepted", result.Kind);

        var view = await TestServerHost.WaitForViewAsync(storyteller, v => v.SlotIndex >= 1, TimeSpan.FromSeconds(5));
        Assert.NotNull(view);
        Assert.True(view!.SlotIndex >= 1);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var start = events.Single(item => item.Event is SlotEnteredEvent { SlotIndex: 0 }).RecordedAt;
        var next = events.Single(item => item.Event is SlotEnteredEvent { SlotIndex: 1 }).RecordedAt;

        Assert.True(
            (next - start).TotalSeconds >= quota * 0.7,
            $"作废缩短了槽位：{(next - start).TotalSeconds:F3}s < {quota}s（D-0013 §4）");
    }

    /// <summary>行 20：客户端能提供的只有幂等键与序号，没有任何时间权威——节奏仍由服务端时钟决定。</summary>
    [Fact]
    public async Task ClientSuppliedValues_CannotChangeServerRhythm()
    {
        const double quota = 0.2;
        await using var host = new TestServerHost(quota, seatCount: 2);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();
        Assert.True(await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait));

        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requests.First().RequestId,
            "seat:2",
            "test-clock-1",
            999_999_999L);
        Assert.Equal("Accepted", result.Kind);

        var view = await TestServerHost.WaitForViewAsync(storyteller, v => v.PlanCompleted, TimeSpan.FromSeconds(10));
        Assert.True(view?.PlanCompleted == true);

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var entered = events
            .Where(item => item.Event is SlotEnteredEvent)
            .Select(item => item.RecordedAt)
            .ToArray();
        var completedAt = events
            .Single(item => item.Event is PhaseCompletedEvent { PlanLabel: "demo:night-1" })
            .RecordedAt;
        var durations = Durations([.. entered, completedAt]);

        Assert.All(
            durations,
            duration => Assert.True(
                duration.TotalSeconds >= quota * 0.7,
                $"客户端传入的序号/time 影响了服务端节奏：{duration.TotalSeconds:F3}s（D-0013 §6）"));

        // 服务端序号自增且连续；客户端传来的 999999999 不得出现在事件序号里
        var sequences = events.Select(item => item.Sequence).ToArray();
        Assert.Equal(1, sequences[0]);
        for (var index = 1; index < sequences.Length; index++)
        {
            Assert.Equal(sequences[index - 1] + 1, sequences[index]);
        }

        Assert.DoesNotContain(999_999_999L, sequences);
        Assert.True(result.Sequence > 0 && result.Sequence < 999_999_999L, "服务端序号必须由服务端决定");
    }

    /// <summary>行 12：行动者全部不在场与全部在场，客户端可观测时间线一致。</summary>
    [Fact]
    public async Task NightTimeline_IsIndependentOfActorAvailability()
    {
        const double quota = 0.2;

        var withActor = await MeasureControlledNightAsync(quota, withActor: true);
        var withoutActor = await MeasureControlledNightAsync(quota, withActor: false);

        Assert.True(
            Math.Abs((withActor - withoutActor).TotalMilliseconds) < 400,
            $"两条时间线差异过大：有行动 {withActor.TotalSeconds:F3}s / 无行动 {withoutActor.TotalSeconds:F3}s（D-0013 可测性）");
    }

    private static TimeSpan[] Durations(DateTimeOffset[] timeline) =>
        [.. timeline.Zip(timeline.Skip(1), (start, end) => end - start)];

    /// <summary>跑一条受控的单槽位夜：行动者在场则发请求并秒回；不在场则是空槽位。</summary>
    private static async Task<TimeSpan> MeasureControlledNightAsync(double quota, bool withActor)
    {
        await using var host = new TestServerHost(quota, seatCount: 2);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        var observerMessages = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2), observerMessages.Enqueue);
        await using var storyteller = await host.ConnectStorytellerAsync();

        // 先把演示夜强推走完（两个变体一致处理，不影响被测时间线）
        var demoView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var guard = 0;
        while (!demoView.PlanCompleted && guard++ < 10)
        {
            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：清理演示夜",
                $"test-clear-{guard}");
            Assert.Equal("Accepted", forced.Kind);
            demoView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        }

        var label = withActor ? "test:actor" : "test:no-actor";
        var actionSlot = withActor
            ? StepSlot.Action(
                new StepSlotId("slot-1"),
                new SeatId(1),
                new ChoicePrompt
                {
                    Context = "受控测试选择",
                    Options = [new DecisionOption { Value = "a", Preview = "选 a" }],
                    OnNoOption = NoOptionBehavior.Skip,
                })
            : StepSlot.Empty(new StepSlotId("slot-1"));
        var plan = new StepPlan
        {
            Label = label,
            Phase = GamePhase.OtherNight,
            Slots = [actionSlot, StepSlot.DawnWait(new StepSlotId("dawn"))],
        };

        var started = await host.ExecuteHostCommandAsync(
            new StartPhaseCommand { Plan = plan },
            $"test-start-{label}",
            CancellationToken.None);
        Assert.Equal(CommandResultKind.Accepted, started.Kind);

        var expectedRequests = withActor ? 2 : 1; // 演示夜 1 条 + 受控夜 1 条
        if (withActor)
        {
            Assert.True(
                await TestServerHost.WaitUntilAsync(() => requests.Count >= expectedRequests, Wait),
                "行动槽位应发出请求");
            var request = requests.ToArray()[^1];
            var answered = await seat1.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                request.RequestId,
                "a",
                $"test-answer-{label}",
                1L);
            Assert.Equal("Accepted", answered.Kind);
        }

        var completed = await TestServerHost.WaitForViewAsync(
            storyteller,
            v => v.PlanCompleted && v.Sequence > started.Sequence);
        Assert.True(completed?.PlanCompleted == true, "受控夜应走完");

        var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var phaseStart = events
            .Single(item => item.Event is PhaseStartedEvent phase && phase.Plan.Label == label)
            .RecordedAt;
        var phaseEnd = events
            .Single(item => item.Event is PhaseCompletedEvent phase && phase.PlanLabel == label)
            .RecordedAt;

        // 非当事玩家（2 号）在两个变体里都不应观察到任何活动（D-0013 §5）
        Assert.Empty(observerMessages);
        return phaseEnd - phaseStart;
    }
}
