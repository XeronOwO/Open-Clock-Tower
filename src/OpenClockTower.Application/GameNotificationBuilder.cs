using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>把产出的事件翻译成需要推送的通知（推送在事件提交**之后**发出）。</summary>
public static class GameNotificationBuilder
{
    /// <summary>翻译一次提交产出的事件；总是附带一条说书人视图变更通知。</summary>
    public static IReadOnlyList<GameNotification> Build(
        IReadOnlyList<GameEvent> events,
        StepMachineState? previousMachine)
    {
        var notifications = new List<GameNotification>();
        foreach (var gameEvent in events)
        {
            switch (gameEvent)
            {
                case OperationRequestIssuedEvent issued:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.OperationRequestIssued,
                        Seat = issued.Request.Addressee,
                        Request = issued.Request,
                    });
                    break;

                case InformationResultIssuedEvent information:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.InformationResultIssued,
                        Seat = information.Recipient,
                        Information = information,
                    });
                    break;

                case OperationRequestVoidedEvent voided:
                    var addressee = FindAddressee(events, previousMachine, voided.RequestId);
                    if (addressee is { } seat)
                    {
                        notifications.Add(new GameNotification
                        {
                            Kind = GameNotificationKind.OperationRequestVoided,
                            Seat = seat,
                            RequestId = voided.RequestId,
                            Void = voided.Void,
                        });
                    }

                    break;

                // 玩家本人作答与说书人代填走同一条通知：收件人始终是请求的行动者，
                // 「谁做出的决定」留在 Answer.Source 里（票据行 6 的可审计口径）。
                case OperationRequestAnsweredEvent answered:
                    var answeredAddressee = FindAddressee(events, previousMachine, answered.RequestId);
                    if (answeredAddressee is { } answeredSeat)
                    {
                        notifications.Add(new GameNotification
                        {
                            Kind = GameNotificationKind.OperationRequestAnswered,
                            Seat = answeredSeat,
                            RequestId = answered.RequestId,
                            Answer = answered.Answer,
                        });
                    }

                    break;

                // 阶段开始是公开信息：不带席位 → 分发器广播给全部已绑定席位。
                case PhaseStartedEvent started:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.PhaseStarted,
                        Phase = started.Plan.Phase,
                    });
                    break;
            }
        }

        notifications.Add(new GameNotification { Kind = GameNotificationKind.StorytellerViewChanged });
        return notifications;
    }

    private static SeatId? FindAddressee(
        IReadOnlyList<GameEvent> events,
        StepMachineState? previousMachine,
        OperationRequestId requestId)
    {
        foreach (var gameEvent in events)
        {
            if (gameEvent is OperationRequestIssuedEvent issued && issued.Request.Id == requestId)
            {
                return issued.Request.Addressee;
            }
        }

        var pending = previousMachine?.PendingRequest;
        return pending is not null && pending.Id == requestId ? pending.Addressee : null;
    }
}
