using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 推送分发：按通知类型**定向单播**到正确的连接（D-0013 §5：其他人没有任何活动指示）。
/// </summary>
/// <remarks>
/// 两种广播面：说书人视图变更推给全部说书人连接；阶段开始是公开信息，推给全部已绑定席位。
/// 其余通知（请求 / 响应 / 作废 / 信息）一律只到当事玩家的连接。
/// </remarks>
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

    /// <summary>把提交后的通知推出去；没有在线连接时记日志（重连时补齐 / 重投）。</summary>
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
                            .ReceiveOperationRequest(ProjectionMapper.ToDto(request, notification.Sequence));
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
                            .ReceiveOperationRequestVoided(ProjectionMapper.ToDto(requestId, voided, notification.Sequence));
                        _logger.LogInformation(
                            "已推送请求作废：seat={Seat} request={RequestId} reason={Reason}",
                            voidedSeat,
                            requestId,
                            voided.Reason);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "请求作废无在线连接（重连时按事件补齐）：seat={Seat} request={RequestId}",
                            voidedSeat,
                            requestId);
                    }

                    break;

                case GameNotificationKind.OperationRequestAnswered
                    when notification.Seat is { } answeredSeat
                         && notification.RequestId is { } answeredRequestId
                         && notification.Answer is { } answer:
                    if (_registry.TryGetSeatConnection(answeredSeat, out var answeredConnectionId))
                    {
                        await _hub.Clients.Client(answeredConnectionId)
                            .ReceiveOperationRequestAnswered(ProjectionMapper.ToDto(answeredRequestId, answer, notification.Sequence));
                        _logger.LogInformation(
                            "已推送请求响应：seat={Seat} request={RequestId} source={Source}",
                            answeredSeat,
                            answeredRequestId,
                            answer.Source);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "请求响应无在线连接（重连时按事件补齐）：seat={Seat} request={RequestId}",
                            answeredSeat,
                            answeredRequestId);
                    }

                    break;

                case GameNotificationKind.InformationResultIssued
                    when notification.Seat is { } informationSeat
                         && notification.Information is { } information:
                    if (_registry.TryGetSeatConnection(informationSeat, out var informationConnectionId))
                    {
                        await _hub.Clients.Client(informationConnectionId)
                            .ReceiveInformationResult(ProjectionMapper.ToDto(information, notification.Sequence));
                        _logger.LogInformation(
                            "已推送信息结果：seat={Seat} ability={Ability}",
                            informationSeat,
                            information.Ability);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "信息结果无在线连接（玩家重连时按序号补齐）：seat={Seat} ability={Ability}",
                            informationSeat,
                            information.Ability);
                    }

                    break;

                case GameNotificationKind.PhaseStarted when notification.Phase is { } startedPhase:
                    await PushPhaseStartedAsync(startedPhase, notification.Sequence, cancellationToken);
                    break;

                // 白天是公开信息：按席位投影后各推一份（含"我现在能不能动"）。
                case GameNotificationKind.DayChanged:
                    await PushDayChangedAsync(cancellationToken);
                    break;

                case GameNotificationKind.StorytellerViewChanged:
                case GameNotificationKind.RoomRebuilt:
                    await PushStorytellerViewAsync(cancellationToken);
                    break;
            }
        }
    }

    /// <summary>把阶段开始广播给全部已绑定席位的连接；未连接玩家重连时从快照取（公开信息）。</summary>
    private async Task PushPhaseStartedAsync(GamePhase phase, long sequence, CancellationToken cancellationToken)
    {
        var dto = ProjectionMapper.ToDto(phase, sequence);
        var seats = _registry.Seats;
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_registry.TryGetSeatConnection(seat, out var connectionId))
            {
                await _hub.Clients.Client(connectionId).ReceivePhaseStarted(dto);
                pushed++;
            }
        }

        _logger.LogInformation(
            "已广播阶段开始：phase={Phase} 推送={Pushed}/{Total}",
            phase,
            pushed,
            seats.Count);
    }

    /// <summary>把白天状态按席位投影广播给已绑定的连接；未连接玩家重连时从快照取同一份事实。</summary>
    private async Task PushDayChangedAsync(CancellationToken cancellationToken)
    {
        var seats = _registry.Seats;
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.TryGetSeatConnection(seat, out var connectionId))
            {
                continue;
            }

            // 白天是"读时状态"：序号取读取到的那份视图的序号（可能比背书事件更新），
            // 客户端只接受序号更大的投影，旧的白天推送不会倒灌。
            var view = _session.GetPlayerView(seat);
            if (view.Day is null)
            {
                continue;
            }

            await _hub.Clients.Client(connectionId).ReceiveDayChanged(ProjectionMapper.ToDto(view.Day, view.Sequence));
            pushed++;
        }

        _logger.LogInformation("已广播白天状态：推送={Pushed}/{Total}", pushed, seats.Count);
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
