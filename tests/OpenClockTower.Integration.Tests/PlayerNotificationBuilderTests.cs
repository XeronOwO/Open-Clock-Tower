using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 通知构建的纯函数行为：**每一类玩家可见事件都必须有在线推送通道**，
/// 定向通知带对收件人、阶段广播不带席位（在线推送与重连白名单讲同一份事实）；
/// 每条通知还必须携带**背书事件的事件流序号**（票据 player-information-resync-race：
/// 客户端靠它和快照序号比较先后、按序号合并，推送无序号就会被补齐响应覆盖）。
/// </summary>
/// <remarks>
/// 依据 D-0010 / 架构 §5：重连补齐按 <c>PlayerEventKind</c> 白名单投影；在线推送若漏掉某一类，
/// 玩家只能在重连后看到它——票面第 1–3 条就是这种缺口。这里把"两类事件必须一一对应"锁成测试。
/// </remarks>
public sealed class PlayerNotificationBuilderTests
{
    /// <summary>五类玩家可见事件在同一批里各产出一条对应通知，且方向正确、序号与背书事件一致。</summary>
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
                Draft(11, new OperationRequestIssuedEvent { Request = pending }),
                Draft(12, answeredEvent),
                Draft(13, voidedEvent),
                Draft(14, informationEvent),
                Draft(15, phaseEvent),
            ],
            previousMachine: null,
            publicSurfaceChanged: false);

        var issued = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestIssued);
        Assert.Equal(pending.Addressee, issued.Seat);
        Assert.Equal(11, issued.Sequence);
        var answered = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestAnswered);
        Assert.Equal(pending.Addressee, answered.Seat);
        Assert.Equal(ResponseSource.StorytellerProxy, answered.Answer!.Source);
        Assert.Equal(12, answered.Sequence);
        var voided = notifications.Single(item => item.Kind == GameNotificationKind.OperationRequestVoided);
        Assert.Equal(pending.Addressee, voided.Seat);
        Assert.Equal(13, voided.Sequence);
        var information = notifications.Single(item => item.Kind == GameNotificationKind.InformationResultIssued);
        Assert.Equal(pending.Addressee, information.Seat);
        Assert.Equal(14, information.Sequence);
        var phase = notifications.Single(item => item.Kind == GameNotificationKind.PhaseStarted);
        Assert.Null(phase.Seat);
        Assert.Equal(GamePhase.FirstNight, phase.Phase);
        Assert.Equal(15, phase.Sequence);
    }

    /// <summary>找不到请求的收件人时不猜：不产出定向通知，但说书人视图变更照旧（并带上序号）。</summary>
    [Fact]
    public void AnsweredWithoutKnownAddressee_ProducesNoSeatDirectedNotification()
    {
        var notifications = GameNotificationBuilder.Build(
            [
                Draft(
                    3,
                    new OperationRequestAnsweredEvent
                    {
                        RequestId = new OperationRequestId("unknown-request"),
                        Answer = new OperationRequestAnswer
                        {
                            OptionValue = "seat:2",
                            Source = ResponseSource.Player,
                        },
                    }),
            ],
            previousMachine: null,
            publicSurfaceChanged: false);

        Assert.DoesNotContain(notifications, item => item.Kind == GameNotificationKind.OperationRequestAnswered);
        Assert.Equal(3, notifications.Single(item => item.Kind == GameNotificationKind.StorytellerViewChanged).Sequence);
    }

    /// <summary>白天是公开信息：同批白天事件折算成一条 DayChanged（去重），序号取最后一条白天事件。</summary>
    [Fact]
    public void DayEvents_ProduceASingleDayChangedBroadcast()
    {
        var notifications = GameNotificationBuilder.Build(
            [
                Draft(1, new DayStartedEvent { DayNumber = 1 }),
                Draft(
                    2,
                    new NominationMadeEvent
                    {
                        DayNumber = 1,
                        NominationIndex = 1,
                        Nominator = new SeatId(1),
                        Nominee = new SeatId(2),
                    }),
                Draft(
                    3,
                    new VoteCastEvent
                    {
                        DayNumber = 1,
                        NominationIndex = 1,
                        Voter = new SeatId(1),
                        Voted = true,
                    }),
                Draft(4, new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day }),
                Draft(5, new DayClosedEvent { DayNumber = 1 }),
            ],
            previousMachine: null,
            publicSurfaceChanged: false);

        var dayChanged = notifications.Single(item => item.Kind == GameNotificationKind.DayChanged);
        Assert.Equal(5, dayChanged.Sequence);
        Assert.Null(dayChanged.Seat);
        Assert.Equal(5, notifications.Single(item => item.Kind == GameNotificationKind.StorytellerViewChanged).Sequence);
    }

    /// <summary>
    /// 白天上报的生死变化没有白天事件背书：公开面变化本身必须触发一次 DayChanged 读时投影，
    /// 否则"白天即时公开"退化成"下次刷新才看得见"；夜晚挂起不变化、不推（D-0013 §5）。
    /// </summary>
    [Fact]
    public void PublicSurfaceChangeWithoutDayEvents_StillProducesADayChangedBroadcast()
    {
        var notifications = GameNotificationBuilder.Build(
            [
                Draft(
                    7,
                    new SeatStateChangedEvent
                    {
                        Seat = new SeatId(2),
                        Life = LifeState.Dead,
                        Reason = "说书人裁定",
                    }),
            ],
            previousMachine: null,
            publicSurfaceChanged: true);

        var dayChanged = notifications.Single(item => item.Kind == GameNotificationKind.DayChanged);
        Assert.Equal(7, dayChanged.Sequence);

        var quiet = GameNotificationBuilder.Build(
            [
                Draft(
                    8,
                    new SeatStateChangedEvent
                    {
                        Seat = new SeatId(2),
                        Life = LifeState.Dead,
                        Reason = "夜晚击杀：公开面仍是挂起态",
                    }),
            ],
            previousMachine: null,
            publicSurfaceChanged: false);

        Assert.DoesNotContain(quiet, item => item.Kind == GameNotificationKind.DayChanged);
    }

    /// <summary>测试用草案：只关心序号与事件本身，记录时刻统一取纪元。</summary>
    private static StoredEventDraft Draft(long sequence, GameEvent @event) => new()
    {
        Sequence = sequence,
        Event = @event,
        RecordedAt = DateTimeOffset.FromUnixTimeSeconds(0),
    };
}
