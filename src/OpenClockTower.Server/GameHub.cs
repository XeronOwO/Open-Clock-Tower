using System.Runtime.CompilerServices;
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
/// 本层只做"翻译"：票据 / 凭据 → 身份、wire 参数 → 命令、结果 → DTO / 推送；
/// 一切领域判定都在 Application / Kernel（架构 §1：Server 不做领域判断）。
/// </para>
/// <para>
/// 零信任（D-0012 §4.1）：Join 下发**连接级凭据**，此后每条命令的第一个参数都是它；
/// 凭据只在签发它的那条连接上有效——旧连接的凭据在新连接上会被拒，必须重新出示票据加入。
/// 凭据明文不进日志，只记短指纹；身份完全由服务端持有的凭据记录推导，客户端声明的身份一律不认。
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

    /// <summary>玩家加入 / 重连：票据定位席位，签发连接凭据，返回重连包并**重投**未响应请求。</summary>
    public async Task<SeatJoinDto> JoinSeat(string ticket, long lastSequence)
    {
        var setup = await LoadSetupAsync();
        var seatTicket = setup.Seats.FirstOrDefault(
            item => string.Equals(item.Ticket, ticket, StringComparison.Ordinal));
        if (seatTicket is null)
        {
            _logger.LogWarning("加入被拒：席位票据无效 connection={ConnectionId}", Context.ConnectionId);
            throw new HubException("会话票据无效");
        }

        var credential = _registry.IssueForSeat(seatTicket.Seat, Context.ConnectionId);
        _logger.LogInformation(
            "已签发连接凭据：seat={Seat} connection={ConnectionId} 指纹={Fingerprint}（重连需重新出示票据）",
            seatTicket.Seat,
            Context.ConnectionId,
            ConnectionCredential.FingerprintOf(credential.Value));

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

        return new SeatJoinDto
        {
            Credential = credential.Value,
            Bundle = ProjectionMapper.ToDto(bundle),
        };
    }

    /// <summary>说书人加入：票据定位身份，签发连接凭据（同局同一时刻只保留一条有效说书人连接）。</summary>
    public async Task<StorytellerJoinDto> JoinStoryteller(string ticket)
    {
        var setup = await LoadSetupAsync();
        if (!string.Equals(setup.StorytellerTicket, ticket, StringComparison.Ordinal))
        {
            _logger.LogWarning("说书人加入被拒：票据无效 connection={ConnectionId}", Context.ConnectionId);
            throw new HubException("说书人票据无效");
        }

        var credential = _registry.IssueForStoryteller(Context.ConnectionId);
        _logger.LogInformation(
            "已签发说书人连接凭据：connection={ConnectionId} 指纹={Fingerprint}（旧说书人连接已作废）",
            Context.ConnectionId,
            ConnectionCredential.FingerprintOf(credential.Value));

        var view = ProjectionMapper.ToDto(_session.GetStorytellerView());
        _logger.LogInformation(
            "说书人已加入：connection={ConnectionId} 序号={Sequence} 挂起={Held}",
            Context.ConnectionId,
            view.Sequence,
            view.Pending is not null);

        return new StorytellerJoinDto
        {
            Credential = credential.Value,
            View = view,
        };
    }

    /// <summary>玩家提交响应。</summary>
    public Task<CommandResultDto> SubmitResponse(
        string credential,
        string requestId,
        string optionValue,
        string idempotencyKey,
        long clientSequence) =>
        ExecuteAsync(
            ResolveActor(credential),
            new SubmitResponseCommand
            {
                RequestId = new OperationRequestId(requestId),
                OptionValue = optionValue,
            },
            idempotencyKey,
            clientSequence);

    /// <summary>说书人强制作废。</summary>
    public Task<CommandResultDto> VoidRequest(
        string credential,
        string requestId,
        string reason,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new VoidRequestCommand
            {
                RequestId = new OperationRequestId(requestId),
                Reason = ParseVoidReason(reason),
                Note = note,
            },
            idempotencyKey);

    /// <summary>说书人代填。</summary>
    public Task<CommandResultDto> ProxyFill(
        string credential,
        string requestId,
        string optionValue,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new ProxyFillCommand
            {
                RequestId = new OperationRequestId(requestId),
                OptionValue = optionValue,
                Note = note,
            },
            idempotencyKey);

    /// <summary>说书人强推当前槽位（D-0014 兜底）。</summary>
    public Task<CommandResultDto> ForceAdvance(string credential, string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new ForceAdvanceCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人接管。</summary>
    public Task<CommandResultDto> TakeOver(string credential, string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new TakeOverCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人交还自动化。</summary>
    public Task<CommandResultDto> ReleaseControl(string credential, string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new ReleaseControlCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人了结裁定点（R-0009 自由决定）。</summary>
    public Task<CommandResultDto> ResolveDecisionPoint(
        string credential,
        string decisionPointId,
        string? decision,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
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
        string credential,
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
        // 先过凭据闸再解析参数：未认证连接不该用畸形参数触发异常与日志噪声。
        var actor = ResolveActor(credential);

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
            throw InvalidPayload("至少需要给出一个观测到的状态维度（生死 / 角色 / 阵营 / 醉酒 / 中毒）");
        }

        return ExecuteAsync(
            actor,
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
        string credential,
        SeatCharacterAssignmentDto[] assignments,
        string idempotencyKey)
    {
        var actor = ResolveActor(credential);

        if (assignments is null)
        {
            throw InvalidPayload("分配列表不能为空");
        }

        var mapped = assignments
            .Select(item => new SeatCharacterAssignment
            {
                Seat = new SeatId(item.Seat),
                Character = new CharacterId(item.Character),
            })
            .ToArray();

        return ExecuteAsync(
            actor,
            new AssignCharactersCommand { Assignments = mapped },
            idempotencyKey);
    }

    /// <summary>说书人 / 宿主开启夜晚：服务端按规则表建表（口径是引擎输入，R-0014）。</summary>
    public Task<CommandResultDto> StartNight(
        string credential,
        int nightNumber,
        string variant,
        string idempotencyKey)
    {
        var actor = ResolveActor(credential);

        // 只认名字不认数字：给 Enum.TryParse 传数字会把序号当口径（与零信任相悖）。
        if (!Enum.TryParse<NightOrderVariant>(variant, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw InvalidPayload($"未知的夜晚顺序口径：{variant}（只接受 Original / Recommended）");
        }

        return ExecuteAsync(
            actor,
            new StartNightCommand { NightNumber = nightNumber, Variant = parsed },
            idempotencyKey);
    }

    /// <summary>说书人 / 宿主按事件日志重建房间（D-0014 恢复）。</summary>
    public Task<CommandResultDto> RebuildRoom(string credential, string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new RebuildRoomCommand { Reason = reason }, idempotencyKey);

    /// <summary>说书人查询当前视图（变更时同时会推送，客户端不需要轮询）。</summary>
    public StorytellerViewDto GetStorytellerView(string credential)
    {
        _ = ResolveStorytellerActor(credential);
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
            _logger.LogWarning(
                "命令被拒绝（会话）：connection={ConnectionId} 原因=本局还没有会话信息",
                Context.ConnectionId);
            throw new HubException("本局还没有会话信息");
        }

        return setup;
    }

    /// <summary>
    /// 凭据 → 身份（唯一的身份来源；D-0012：客户端声明一律不认）。
    /// 凭据无效直接拒绝，且**不触达 Application**；通过后由四道闸判"这个身份能不能发这条命令"。
    /// </summary>
    private Actor ResolveActor(string? credential, [CallerMemberName] string method = "")
    {
        var validation = ValidateCredential(credential, method);
        if (!validation.Accepted)
        {
            throw new HubException("连接凭据无效：请先用票据加入（D-0012）");
        }

        return validation.Kind == ActorKind.Player && validation.Seat is { } seat
            ? Actor.Player(seat)
            : Actor.Storyteller();
    }

    /// <summary>查询类入口要求说书人身份（查询不属于命令，不走四道闸）。</summary>
    private Actor ResolveStorytellerActor(string? credential, [CallerMemberName] string method = "")
    {
        var actor = ResolveActor(credential, method);
        if (actor.Kind != ActorKind.Storyteller)
        {
            _logger.LogWarning(
                "查询被拒（身份）：connection={ConnectionId} 方法={Method} 原因=玩家连接不能读说书人视图",
                Context.ConnectionId,
                method);
            throw new HubException("当前连接不是有效的说书人连接（D-0012）");
        }

        return actor;
    }

    /// <summary>校验凭据并审计失败（谁、哪条连接、什么方法、凭据短指纹、原因）——绝不写凭据明文。</summary>
    private CredentialValidation ValidateCredential(string? credential, string method)
    {
        var presented = new ConnectionCredential(credential ?? string.Empty);
        var validation = _registry.Validate(presented, Context.ConnectionId);
        if (!validation.Accepted)
        {
            _logger.LogWarning(
                "命令被拒绝（凭据闸）：connection={ConnectionId} 方法={Method} 指纹={Fingerprint} 原因={Reason}",
                Context.ConnectionId,
                method,
                ConnectionCredential.FingerprintOf(credential),
                validation.Reason);
        }

        return validation;
    }

    /// <summary>参数层拒绝：同样写审计（矩阵行 11：每次拒绝都有可定位记录），且不触达 Application。</summary>
    private HubException InvalidPayload(string reason, [CallerMemberName] string method = "")
    {
        _logger.LogWarning(
            "命令被拒绝（参数）：connection={ConnectionId} 方法={Method} 原因={Reason}",
            Context.ConnectionId,
            method,
            reason);
        return new HubException(reason);
    }

    /// <summary>把客户端传来的维度字符串解析成枚举；null = 本次未观测，非法值当场拒绝（并写审计）。</summary>
    private TEnum? ParseDimension<TEnum>(string? raw, string label)
        where TEnum : struct, Enum
    {
        if (raw is null)
        {
            return null;
        }

        // 只认名字不认数字：Enum.TryParse 会把 "0" 解析成首个枚举值，那是"客户端说了算"，与零信任相悖。
        if (raw.Length == 0 || char.IsAsciiDigit(raw[0]))
        {
            throw InvalidPayload($"未知的{label}：{raw}（只接受枚举名）");
        }

        if (!Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw InvalidPayload($"未知的{label}：{raw}");
        }

        return value;
    }

    private static OperationRequestVoidReason ParseVoidReason(string reason) =>
        Enum.TryParse<OperationRequestVoidReason>(reason, ignoreCase: false, out var parsed)
            ? parsed
            : (OperationRequestVoidReason)(-1);
}
