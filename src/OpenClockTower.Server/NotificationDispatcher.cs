using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 推送分发：按通知类型**定向单播**到正确的连接（D-0013 §5：其他人没有任何活动指示）。
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly ConnectionRegistry _registry;
    private readonly GameSession _session;
    private readonly ILogger<NotificationDispatcher> _logger;

    /// <summary>构造分发器。</summary>
    public NotificationDispatcher(
        IHubContext<GameHub, IGameClient> hub,
        ConnectionRegistry registry,
        GameSession session,
        ILogger<NotificationDispatcher> logger)
    {
        _hub = hub;
        _registry = registry;
        _session = session;
        _logger = logger;
    }

    /// <summary>把提交后的通知推出去；没有在线连接时静默跳过（重连时补齐 / 重投）。</summary>
    public async Task DispatchAsync(CommandResult result, CancellationToken cancellationToken)
    {
        foreach (var notification in result.Notifications)
        {
            switch (notification.Kind)
            {
                case GameNotificationKind.OperationRequestIssued
                    when notification.Seat is { } issuedSeat && notification.Request is { } request:
                    if (_registry.TryGetSeatConnection(issuedSeat, out var requestConnectionId))
                    {
                        await _hub.Clients.Client(requestConnectionId)
                            .ReceiveOperationRequest(ProjectionMapper.ToDto(request));
                        _logger.LogInformation(
                            "已推送操作请求：seat={Seat} request={RequestId} connection={ConnectionId}",
                            issuedSeat,
                            request.Id,
                            requestConnectionId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "操作请求无在线连接（等待玩家重连后重投）：seat={Seat} request={RequestId}",
                            issuedSeat,
                            request.Id);
                    }

                    break;

                case GameNotificationKind.OperationRequestVoided
                    when notification.Seat is { } voidedSeat && notification.RequestId is { } requestId && notification.Void is { } voided:
                    if (_registry.TryGetSeatConnection(voidedSeat, out var voidedConnectionId))
                    {
                        await _hub.Clients.Client(voidedConnectionId)
                            .ReceiveOperationRequestVoided(ProjectionMapper.ToDto(requestId, voided));
                        _logger.LogInformation(
                            "已推送请求作废：seat={Seat} request={RequestId} reason={Reason}",
                            voidedSeat,
                            requestId,
                            voided.Reason);
                    }

                    break;

                case GameNotificationKind.StorytellerViewChanged:
                case GameNotificationKind.RoomRebuilt:
                    await PushStorytellerViewAsync(cancellationToken);
                    break;
            }
        }
    }

    private async Task PushStorytellerViewAsync(CancellationToken cancellationToken)
    {
        var view = ProjectionMapper.ToDto(_session.GetStorytellerView());
        foreach (var connectionId in _registry.StorytellerConnections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _hub.Clients.Client(connectionId).ReceiveStorytellerViewChanged(view);
        }
    }
}
