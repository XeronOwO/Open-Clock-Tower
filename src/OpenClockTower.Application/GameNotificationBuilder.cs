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
