using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 推送分发：按通知类型**定向单播**到正确的连接（D-0013 §5：其他人没有任何活动指示）。
/// </summary>
/// <remarks>
/// <para>
/// 两种广播面：说书人视图变更推给**本桌**全部说书人连接；阶段开始是公开信息，推给**本桌**全部已绑定席位。
/// 其余通知（请求 / 响应 / 作废 / 信息）一律只到当事玩家的连接。
/// </para>
/// <para>
/// **多桌（D-0024）**：会话与席位表都按桌取（<see cref="GameInstance"/>）。
/// 用全局席位表会把甲桌的裁定推到乙桌坐在同一席位号的人手上——这不是"顺手的小事"，
/// 而是信息隔离的破口，所以本类的每个推送入口都要求先给"哪一桌"。
/// </para>
/// </remarks>
public sealed class NotificationDispatcher
{
    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly ConnectionRegistry _registry;
    private readonly ILogger<NotificationDispatcher> _logger;

    /// <summary>构造分发器。</summary>
    public NotificationDispatcher(
        IHubContext<GameHub, IGameClient> hub,
        ConnectionRegistry registry,
        ILogger<NotificationDispatcher> logger)
    {
        _hub = hub;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>把提交后的通知推出去；没有在线连接时记日志（重连时补齐 / 重投）。</summary>
    /// <param name="game">本次提交属于哪一桌（推送范围严格限定在它之内）。</param>
    /// <param name="result">命令结果（含通知）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task DispatchAsync(
        GameInstance game,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        foreach (var notification in result.Notifications)
        {
            switch (notification.Kind)
            {
                case GameNotificationKind.OperationRequestIssued
                    when notification.Seat is { } issuedSeat && notification.Request is { } request:
                    if (_registry.TryGetSeatConnection(game.GameId, issuedSeat, out var requestConnectionId))
                    {
                        await _hub.Clients.Client(requestConnectionId)
                            .ReceiveOperationRequest(ProjectionMapper.ToDto(request, notification.Sequence));
                        _logger.LogInformation(
                            "已推送操作请求：game={GameId} seat={Seat} request={RequestId} connection={ConnectionId}",
                            game.GameId.Value,
                            issuedSeat,
                            request.Id,
                            requestConnectionId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "操作请求无在线连接（等待玩家重连后重投）：game={GameId} seat={Seat} request={RequestId}",
                            game.GameId.Value,
                            issuedSeat,
                            request.Id);
                    }

                    break;

                case GameNotificationKind.OperationRequestVoided
                    when notification.Seat is { } voidedSeat && notification.RequestId is { } requestId && notification.Void is { } voided:
                    if (_registry.TryGetSeatConnection(game.GameId, voidedSeat, out var voidedConnectionId))
                    {
                        await _hub.Clients.Client(voidedConnectionId)
                            .ReceiveOperationRequestVoided(ProjectionMapper.ToDto(requestId, voided, notification.Sequence));
                        _logger.LogInformation(
                            "已推送请求作废：game={GameId} seat={Seat} request={RequestId} reason={Reason}",
                            game.GameId.Value,
                            voidedSeat,
                            requestId,
                            voided.Reason);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "请求作废无在线连接（重连时按事件补齐）：game={GameId} seat={Seat} request={RequestId}",
                            game.GameId.Value,
                            voidedSeat,
                            requestId);
                    }

                    break;

                case GameNotificationKind.OperationRequestAnswered
                    when notification.Seat is { } answeredSeat
                         && notification.RequestId is { } answeredRequestId
                         && notification.Answer is { } answer:
                    if (_registry.TryGetSeatConnection(game.GameId, answeredSeat, out var answeredConnectionId))
                    {
                        await _hub.Clients.Client(answeredConnectionId)
                            .ReceiveOperationRequestAnswered(ProjectionMapper.ToDto(answeredRequestId, answer, notification.Sequence));
                        _logger.LogInformation(
                            "已推送请求响应：game={GameId} seat={Seat} request={RequestId} source={Source}",
                            game.GameId.Value,
                            answeredSeat,
                            answeredRequestId,
                            answer.Source);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "请求响应无在线连接（重连时按事件补齐）：game={GameId} seat={Seat} request={RequestId}",
                            game.GameId.Value,
                            answeredSeat,
                            answeredRequestId);
                    }

                    break;

                case GameNotificationKind.InformationResultIssued
                    when notification.Seat is { } informationSeat
                         && notification.Information is { } information:
                    if (_registry.TryGetSeatConnection(game.GameId, informationSeat, out var informationConnectionId))
                    {
                        await _hub.Clients.Client(informationConnectionId)
                            .ReceiveInformationResult(ProjectionMapper.ToDto(information, notification.Sequence));
                        _logger.LogInformation(
                            "已推送信息结果：game={GameId} seat={Seat} ability={Ability}",
                            game.GameId.Value,
                            informationSeat,
                            information.Ability);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "信息结果无在线连接（玩家重连时按序号补齐）：game={GameId} seat={Seat} ability={Ability}",
                            game.GameId.Value,
                            informationSeat,
                            information.Ability);
                    }

                    break;

                case GameNotificationKind.PhaseStarted when notification.Phase is { } startedPhase:
                    await PushPhaseStartedAsync(game, startedPhase, notification.Sequence, cancellationToken);
                    break;

                // 本人视图变更：按席位推一份整视图（快照口径；未连接玩家重连时从快照取同一份事实）。
                case GameNotificationKind.PlayerViewChanged:
                    await PushPlayerViewChangedAsync(game, notification.Seat, cancellationToken);
                    break;

                // 白天是公开信息：按席位投影后各推一份（含"我现在能不能动"）。
                case GameNotificationKind.DayChanged:
                    await PushDayChangedAsync(game, cancellationToken);
                    break;

                // 游戏结束与呆瓜的公开选择都是公开事实：广播给本桌全部已绑定席位（R-0024 / R-0027）。
                case GameNotificationKind.GameEnded when notification.Outcome is { } endedOutcome:
                    await PushGameEndedAsync(game, endedOutcome, notification.Sequence, cancellationToken);
                    break;

                case GameNotificationKind.KlutzChoiceMade when notification.KlutzChoice is { } klutzChoice:
                    await PushKlutzChoiceMadeAsync(game, klutzChoice, notification.Sequence, cancellationToken);
                    break;

                case GameNotificationKind.StorytellerViewChanged:
                case GameNotificationKind.RoomRebuilt:
                    await PushStorytellerViewAsync(game, cancellationToken);
                    break;
            }
        }
    }

    /// <summary>把阶段开始广播给本桌全部已绑定席位的连接；未连接玩家重连时从快照取（公开信息）。</summary>
    private async Task PushPhaseStartedAsync(
        GameInstance game,
        GamePhase phase,
        long sequence,
        CancellationToken cancellationToken)
    {
        var dto = ProjectionMapper.ToDto(phase, sequence);
        var seats = _registry.SeatsOf(game.GameId);
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_registry.TryGetSeatConnection(game.GameId, seat, out var connectionId))
            {
                await _hub.Clients.Client(connectionId).ReceivePhaseStarted(dto);
                pushed++;
            }
        }

        _logger.LogInformation(
            "已广播阶段开始：game={GameId} phase={Phase} 推送={Pushed}/{Total}",
            game.GameId.Value,
            phase,
            pushed,
            seats.Count);
    }

    /// <summary>把白天状态按席位投影广播给本桌已绑定的连接；未连接玩家重连时从快照取同一份事实。</summary>
    private async Task PushDayChangedAsync(GameInstance game, CancellationToken cancellationToken)
    {
        var seats = _registry.SeatsOf(game.GameId);
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.TryGetSeatConnection(game.GameId, seat, out var connectionId))
            {
                continue;
            }

            // 白天是"读时状态"：序号取读取到的那份视图的序号（可能比背书事件更新），
            // 客户端只接受序号更大的投影，旧的白天推送不会倒灌。
            var view = game.Session.GetPlayerView(seat);
            if (view.Day is null)
            {
                continue;
            }

            await _hub.Clients.Client(connectionId).ReceiveDayChanged(ProjectionMapper.ToDto(view.Day, view.Sequence));
            pushed++;
        }

        _logger.LogInformation(
            "已广播白天状态：game={GameId} 推送={Pushed}/{Total}",
            game.GameId.Value,
            pushed,
            seats.Count);
    }

    /// <summary>
    /// 把"本人视图"推给本桌指定席位（<paramref name="seat"/> 为 null 时推给本桌全部已绑定席位）：
    /// 投影按席位算，序号取读取到的那份视图的序号（读时状态，与 <see cref="PushDayChangedAsync"/> 同一口径）；
    /// 未连接玩家重连时从快照取同一份事实。
    /// </summary>
    private async Task PushPlayerViewChangedAsync(
        GameInstance game,
        SeatId? seat,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SeatId> targets = seat is { } single ? [single] : _registry.SeatsOf(game.GameId);
        var pushed = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_registry.TryGetSeatConnection(game.GameId, target, out var connectionId))
            {
                continue;
            }

            var view = game.Session.GetPlayerView(target);
            await _hub.Clients.Client(connectionId).ReceivePlayerViewChanged(view.Sequence, ProjectionMapper.ToDto(view));
            pushed++;
        }

        _logger.LogInformation(
            "已推送本人视图：game={GameId} seat={Seat} 推送={Pushed}/{Total}",
            game.GameId.Value,
            seat?.Value,
            pushed,
            targets.Count);
    }

    /// <summary>
    /// 席位名变化（认领 / 改名 / 解除，D-0021）：把最新整视图推给**本桌**全部已绑定席位与说书人。
    /// </summary>
    /// <remarks>名字在本桌内是公开信息；未连接的玩家重连时从快照取同一份事实（会话读模型）。</remarks>
    public async Task PushSeatNamesChangedAsync(GameInstance game, CancellationToken cancellationToken)
    {
        await PushPlayerViewChangedAsync(game, seat: null, cancellationToken);
        await PushStorytellerViewAsync(game, cancellationToken);
    }

    private async Task PushStorytellerViewAsync(GameInstance game, CancellationToken cancellationToken)
    {
        var view = ProjectionMapper.ToDto(game.Session.GetStorytellerView());
        foreach (var connectionId in _registry.StorytellerConnectionsOf(game.GameId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _hub.Clients.Client(connectionId).ReceiveStorytellerViewChanged(view);
        }
    }

    /// <summary>把"本局结束"广播给本桌全部已绑定席位；未连接玩家重连时从快照取同一份结论（R-0024）。</summary>
    private async Task PushGameEndedAsync(
        GameInstance game,
        GameOutcome outcome,
        long sequence,
        CancellationToken cancellationToken)
    {
        var dto = ProjectionMapper.ToDto(outcome, sequence);
        var seats = _registry.SeatsOf(game.GameId);
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_registry.TryGetSeatConnection(game.GameId, seat, out var connectionId))
            {
                await _hub.Clients.Client(connectionId).ReceiveGameEnded(dto);
                pushed++;
            }
        }

        _logger.LogInformation(
            "已广播游戏结束：game={GameId} winner={Winner} condition={Condition} 推送={Pushed}/{Total}",
            game.GameId.Value,
            outcome.Winner,
            outcome.Condition,
            pushed,
            seats.Count);
    }

    /// <summary>把呆瓜的公开选择广播给本桌全部已绑定席位（选择本身就是公开事实，R-0027）。</summary>
    private async Task PushKlutzChoiceMadeAsync(
        GameInstance game,
        KlutzChoiceMadeEvent choice,
        long sequence,
        CancellationToken cancellationToken)
    {
        var dto = ProjectionMapper.ToDto(choice, sequence);
        var seats = _registry.SeatsOf(game.GameId);
        var pushed = 0;
        foreach (var seat in seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_registry.TryGetSeatConnection(game.GameId, seat, out var connectionId))
            {
                await _hub.Clients.Client(connectionId).ReceiveKlutzChoiceMade(dto);
                pushed++;
            }
        }

        _logger.LogInformation(
            "已广播呆瓜选择：game={GameId} seat={Seat} target={Target} 推送={Pushed}/{Total}",
            game.GameId.Value,
            choice.Klutz.Value,
            choice.Target.Value,
            pushed,
            seats.Count);
    }
}
