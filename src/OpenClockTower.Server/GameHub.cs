using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Server;

/// <summary>
/// 游戏 Hub：会话绑定、命令入口、定向推送。
/// </summary>
/// <remarks>
/// <para>
/// 本层只做"翻译"：票据 → 身份、wire 参数 → 命令、结果 → DTO / 推送；一切判定都在
/// Application / Kernel（架构 §1：Server 不做领域判断）。
/// </para>
/// <para>
/// 占位说明：连接级私有凭据、票据撤销与完整负向套件属「零信任」票据；
/// 这里先做到"身份由服务端从票据推导，客户端不能自称"。
/// </para>
/// </remarks>
public sealed class GameHub : Hub<IGameClient>
{
    private readonly IGameCatalog _catalog;
    private readonly GameId _gameId;
    private readonly GameSession _session;
    private readonly ConnectionRegistry _registry;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<GameHub> _logger;

    /// <summary>构造 Hub。</summary>
    public GameHub(
        IGameCatalog catalog,
        GameId gameId,
        GameSession session,
        ConnectionRegistry registry,
        NotificationDispatcher dispatcher,
        ILogger<GameHub> logger)
    {
        _catalog = catalog;
        _gameId = gameId;
        _session = session;
        _registry = registry;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>玩家加入 / 重连：票据定位席位，返回重连包并**重投**未响应请求。</summary>
    public async Task<ReconnectBundleDto> JoinSeat(string ticket, long lastSequence)
    {
        var setup = await LoadSetupAsync();
        var seatTicket = setup.Seats.FirstOrDefault(
            item => string.Equals(item.Ticket, ticket, StringComparison.Ordinal));
        if (seatTicket is null)
        {
            _logger.LogWarning("加入被拒：席位票据无效 connection={ConnectionId}", Context.ConnectionId);
            throw new HubException("会话票据无效");
        }

        _registry.BindSeat(seatTicket.Seat, Context.ConnectionId);
        var bundle = await _session.GetReconnectBundleAsync(seatTicket.Seat, lastSequence, Context.ConnectionAborted);

        if (bundle.View.PendingRequest is { } pending)
        {
            await Clients.Caller.ReceiveOperationRequest(ProjectionMapper.ToDto(pending));
        }

        _logger.LogInformation(
            "玩家已加入：seat={Seat} connection={ConnectionId} 序号={Sequence} 重投请求={Redelivered}",
            seatTicket.Seat,
            Context.ConnectionId,
            bundle.Sequence,
            bundle.View.PendingRequest is not null);

        return ProjectionMapper.ToDto(bundle);
    }

    /// <summary>说书人加入：票据定位身份。</summary>
    public async Task<StorytellerViewDto> JoinStoryteller(string ticket)
    {
        var setup = await LoadSetupAsync();
        if (!string.Equals(setup.StorytellerTicket, ticket, StringComparison.Ordinal))
        {
            _logger.LogWarning("说书人加入被拒：票据无效 connection={ConnectionId}", Context.ConnectionId);
            throw new HubException("说书人票据无效");
        }

        _registry.BindStoryteller(Context.ConnectionId);
        var view = ProjectionMapper.ToDto(_session.GetStorytellerView());
        _logger.LogInformation(
            "说书人已加入：connection={ConnectionId} 序号={Sequence} 挂起={Held}",
            Context.ConnectionId,
            view.Sequence,
            view.Pending is not null);
        return view;
    }

    /// <summary>玩家提交响应。</summary>
    public Task<CommandResultDto> SubmitResponse(string requestId, string optionValue, string idempotencyKey, long clientSequence) =>
        ExecuteAsync(
            ResolvePlayerActor(),
            new SubmitResponseCommand
            {
                RequestId = new OperationRequestId(requestId),
                OptionValue = optionValue,
            },
            idempotencyKey,
            clientSequence);

    /// <summary>说书人强制作废。</summary>
    public Task<CommandResultDto> VoidRequest(string requestId, string reason, string? note, string idempotencyKey) =>
        ExecuteAsync(
            ResolveStorytellerActor(),
            new VoidRequestCommand
            {
                RequestId = new OperationRequestId(requestId),
                Reason = ParseVoidReason(reason),
                Note = note,
            },
            idempotencyKey);

    /// <summary>说书人代填。</summary>
    public Task<CommandResultDto> ProxyFill(string requestId, string optionValue, string? note, string idempotencyKey) =>
        ExecuteAsync(
            ResolveStorytellerActor(),
            new ProxyFillCommand
            {
                RequestId = new OperationRequestId(requestId),
                OptionValue = optionValue,
                Note = note,
            },
            idempotencyKey);

    /// <summary>说书人强推当前槽位（D-0014 兜底）。</summary>
    public Task<CommandResultDto> ForceAdvance(string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveStorytellerActor(), new ForceAdvanceCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人接管。</summary>
    public Task<CommandResultDto> TakeOver(string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveStorytellerActor(), new TakeOverCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人交还自动化。</summary>
    public Task<CommandResultDto> ReleaseControl(string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveStorytellerActor(), new ReleaseControlCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人了结裁定点（R-0009 自由决定）。</summary>
    public Task<CommandResultDto> ResolveDecisionPoint(
        string decisionPointId,
        string? decision,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveStorytellerActor(),
            new ResolveDecisionPointCommand
            {
                DecisionPointId = new DecisionPointId(decisionPointId),
                Decision = decision,
                Note = note,
            },
            idempotencyKey);

    /// <summary>
    /// 说书人上报座位状态变化（含原因与归因；依赖失效时内核自动作废挂起请求）。
    /// 只上报本次观测到的维度，至少给一个；不给的维度不参与判定，也不会进状态账。
    /// </summary>
    public Task<CommandResultDto> ReportSeatState(
        int seat,
        string? life,
        string? character,
        string? alignment,
        string? drunk,
        string? poison,
        string reason,
        int? causedBySeat,
        string idempotencyKey)
    {
        var parsedLife = ParseDimension<LifeState>(life, "生死");
        var parsedAlignment = ParseDimension<Alignment>(alignment, "阵营");
        var parsedDrunk = ParseDimension<DrunkState>(drunk, "醉酒状态");
        var parsedPoison = ParseDimension<PoisonState>(poison, "中毒状态");

        if (parsedLife is null
            && character is null
            && parsedAlignment is null
            && parsedDrunk is null
            && parsedPoison is null)
        {
            throw new HubException("至少需要给出一个观测到的状态维度（生死 / 角色 / 阵营 / 醉酒 / 中毒）");
        }

        return ExecuteAsync(
            ResolveStorytellerActor(),
            new ApplySeatStateCommand
            {
                Seat = new SeatId(seat),
                Life = parsedLife,
                Character = character is null ? null : new CharacterId(character),
                Alignment = parsedAlignment,
                Drunk = parsedDrunk,
                Poison = parsedPoison,
                Reason = reason,
                CausedBy = causedBySeat is { } causer ? new SeatId(causer) : null,
            },
            idempotencyKey);
    }

    /// <summary>
    /// 说书人 / 宿主开局分配：为席位绑定角色，并记录初始生死（仅首个阶段开始前可用）。
    /// </summary>
    /// <remarks>
    /// 本方法只做"翻译"：席位号与角色 slug 都会在 Application 层按会话席位名单与首版花名册
    /// 重新校验（D-0012：客户端声明不可信）。
    /// </remarks>
    public Task<CommandResultDto> AssignCharacters(
        SeatCharacterAssignmentDto[] assignments,
        string idempotencyKey)
    {
        if (assignments is null)
        {
            throw new HubException("分配列表不能为空");
        }

        var mapped = assignments
            .Select(item => new SeatCharacterAssignment
            {
                Seat = new SeatId(item.Seat),
                Character = new CharacterId(item.Character),
            })
            .ToArray();

        return ExecuteAsync(
            ResolveStorytellerActor(),
            new AssignCharactersCommand { Assignments = mapped },
            idempotencyKey);
    }

    /// <summary>说书人 / 宿主开启夜晚：服务端按规则表建表（口径是引擎输入，R-0014）。</summary>
    public Task<CommandResultDto> StartNight(
        int nightNumber,
        string variant,
        string idempotencyKey)
    {
        // 只认名字不认数字：给 Enum.TryParse 传数字会把序号当口径（与零信任相悖）。
        if (!Enum.TryParse<NightOrderVariant>(variant, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new HubException($"未知的夜晚顺序口径：{variant}（只接受 Original / Recommended）");
        }

        return ExecuteAsync(
            ResolveStorytellerActor(),
            new StartNightCommand { NightNumber = nightNumber, Variant = parsed },
            idempotencyKey);
    }

    /// <summary>把客户端传来的维度字符串解析成枚举；null = 本次未观测，非法值当场拒绝。</summary>
    private static TEnum? ParseDimension<TEnum>(string? raw, string label)
        where TEnum : struct, Enum
    {
        if (raw is null)
        {
            return null;
        }

        // 只认名字不认数字：Enum.TryParse 会把 "0" 解析成首个枚举值，那是"客户端说了算"，与零信任相悖。
        if (raw.Length == 0 || char.IsAsciiDigit(raw[0]))
        {
            throw new HubException($"未知的{label}：{raw}（只接受枚举名）");
        }

        if (!Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw new HubException($"未知的{label}：{raw}");
        }

        return value;
    }

    /// <summary>说书人 / 宿主按事件日志重建房间（D-0014 恢复）。</summary>
    public Task<CommandResultDto> RebuildRoom(string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveStorytellerActor(), new RebuildRoomCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人查询当前视图（变更时同时会推送，客户端不需要轮询）。</summary>
    public StorytellerViewDto GetStorytellerView()
    {
        _ = ResolveStorytellerActor();
        return ProjectionMapper.ToDto(_session.GetStorytellerView());
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);
        _logger.LogInformation(
            "连接已断开：connection={ConnectionId} 异常={Exception}",
            Context.ConnectionId,
            exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<CommandResultDto> ExecuteAsync(
        Actor actor,
        GameCommand command,
        string idempotencyKey,
        long clientSequence = 0)
    {
        var result = await _session.ExecuteAsync(
            new CommandEnvelope
            {
                Command = command,
                Actor = actor,
                IdempotencyKey = idempotencyKey,
                ClientSequence = clientSequence,
            },
            Context.ConnectionAborted);

        await _dispatcher.DispatchAsync(result, Context.ConnectionAborted);
        return ProjectionMapper.ToDto(result);
    }

    private async Task<GameSetup> LoadSetupAsync()
    {
        var setup = await _catalog.FindAsync(_gameId, Context.ConnectionAborted);
        if (setup is null)
        {
            throw new HubException("本局还没有会话信息");
        }

        return setup;
    }

    private Actor ResolvePlayerActor()
    {
        var seat = _registry.FindSeat(Context.ConnectionId);
        if (seat is null)
        {
            throw new HubException("当前连接没有绑定席位，请先加入");
        }

        return Actor.Player(seat.Value);
    }

    private Actor ResolveStorytellerActor()
    {
        if (!_registry.IsStoryteller(Context.ConnectionId))
        {
            throw new HubException("当前连接不是说书人连接");
        }

        return Actor.Storyteller();
    }

    private static OperationRequestVoidReason ParseVoidReason(string reason) =>
        Enum.TryParse<OperationRequestVoidReason>(reason, ignoreCase: false, out var parsed)
            ? parsed
            : (OperationRequestVoidReason)(-1);
}
