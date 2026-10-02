using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 通知构建的纯函数行为：**每一类玩家可见事件都必须有在线推送通道**，
/// 定向通知带对收件人、阶段广播不带席位（在线推送与重连白名单讲同一份事实）。
/// </summary>
/// <remarks>
/// 依据 D-0010 / 架构 §5：重连补齐按 <c>PlayerEventKind</c> 白名单投影；在线推送若漏掉某一类，
/// 玩家只能在重连后看到它——票面第 1–3 条就是这种缺口。这里把"两类事件必须一一对应"锁成测试。
/// </remarks>
public sealed class PlayerNotificationBuilderTests
{
    /// <summary>五类玩家可见事件在同一批里各产出一条对应通知，且方向正确。</summary>
    [Fact]
    public void EveryPlayerVisibleEventKind_ProducesALiveNotification()
    {
        var machine = StepMachine.StartPhase(TestNightPlan.CreateFirstNight(seatCount: 3)).State;
        var pending = machine.PendingRequest;
        Assert.NotNull(pending);

        var phaseEvent = new PhaseStartedEvent
        {
            Plan = TestNightPlan.CreateFirstNight(seatCount: 3),
            Control = ControlMode.Automatic,
        };
        var answeredEvent = new OperationRequestAnsweredEvent
        {
            RequestId = pending!.Id,
            Answer = new OperationRequestAnswer
            {
                OptionValue = pending.Prompt.Options[0].Value,
                Source = ResponseSource.StorytellerProxy,
                Note = "测试：代填",
            },
        };
        var voidedEvent = new OperationRequestVoidedEvent
        {
            RequestId = pending.Id,
            Void = new OperationRequestVoid
            {
                Reason = OperationRequestVoidReason.StorytellerForce,
                Note = "测试：强制作废",
            },
        };
        var informationEvent = new InformationResultIssuedEvent
        {
            Recipient = pending.Addressee,
            Ability = new AbilityId("test-ability"),
            Content = "测试信息",
            MayBeFalse = false,
        };

        var notifications = GameNotificationBuilder.Build(
            [
                new OperationRequestIssuedEvent { Request = pending },
                answeredEvent,
                voidedEvent,
                informationEvent,
                phaseEvent,
            ],
            previousMachine: null);

        var issued = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestIssued);
        Assert.Equal(pending.Addressee, issued.Seat);
        var answered = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestAnswered);
        Assert.Equal(pending.Addressee, answered.Seat);
        Assert.Equal(ResponseSource.StorytellerProxy, answered.Answer!.Source);
        var voided = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestVoided);
        Assert.Equal(pending.Addressee, voided.Seat);
        var information = notifications.Single(item => item.Kind == GameNotificationKind.InformationResultIssued);
        Assert.Equal(pending.Addressee, information.Seat);
        var phase = notifications.Single(item => item.Kind == GameNotificationKind.PhaseStarted);
        Assert.Null(phase.Seat);
        Assert.Equal(GamePhase.FirstNight, phase.Phase);
    }

    /// <summary>找不到请求的收件人时不猜：不产出定向通知，但说书人视图变更照旧。</summary>
    [Fact]
    public void AnsweredWithoutKnownAddressee_ProducesNoSeatDirectedNotification()
    {
        var notifications = GameNotificationBuilder.Build(
            [
                new OperationRequestAnsweredEvent
                {
                    RequestId = new OperationRequestId("unknown-request"),
                    Answer = new OperationRequestAnswer
                    {
                        OptionValue = "seat:2",
                        Source = ResponseSource.Player,
                    },
                },
            ],
            previousMachine: null);

        Assert.DoesNotContain(notifications, item => item.Kind == GameNotificationKind.OperationRequestAnswered);
        Assert.Contains(notifications, item => item.Kind == GameNotificationKind.StorytellerViewChanged);
    }

    /// <summary>白天是公开信息：同批白天事件折算成一条 DayChanged（去重），并照旧附带说书人视图变更。</summary>
    [Fact]
    public void DayEvents_ProduceASingleDayChangedBroadcast()
    {
        var notifications = GameNotificationBuilder.Build(
            [
                new DayStartedEvent { DayNumber = 1 },
                new NominationMadeEvent
                {
                    DayNumber = 1,
                    NominationIndex = 1,
                    Nominator = new SeatId(1),
                    Nominee = new SeatId(2),
                },
                new VoteCastEvent
                {
                    DayNumber = 1,
                    NominationIndex = 1,
                    Voter = new SeatId(1),
                    Voted = true,
                },
                new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2) },
                new DayClosedEvent { DayNumber = 1 },
            ],
            previousMachine: null);

        Assert.Single(notifications, item => item.Kind == GameNotificationKind.DayChanged);
        Assert.Contains(notifications, item => item.Kind == GameNotificationKind.StorytellerViewChanged);
        Assert.All(
            notifications.Where(item => item.Kind == GameNotificationKind.DayChanged),
            item => Assert.Null(item.Seat));
    }
}
