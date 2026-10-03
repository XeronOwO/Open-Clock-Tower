using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

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

        try
        {
            var bundle = await _session.GetReconnectBundleAsync(seatTicket.Seat, lastSequence, Context.ConnectionAborted);

            if (bundle.View.PendingRequest is { } pending)
            {
                // 重投的请求状态属于这份快照：序号取快照序号，客户端合并时与快照同源。
                await Clients.Caller.ReceiveOperationRequest(ProjectionMapper.ToDto(pending, bundle.View.Sequence));
            }

            _logger.LogInformation(
                "玩家已加入：seat={Seat} connection={ConnectionId} 快照序号={Sequence} 本地已知={KnownSequence} 重投请求={Redelivered}",
                seatTicket.Seat,
                Context.ConnectionId,
                bundle.Sequence,
                lastSequence,
                bundle.View.PendingRequest is not null);

            return new SeatJoinDto
            {
                Credential = credential.Value,
                Bundle = ProjectionMapper.ToDto(bundle),
            };
        }
        catch (InvalidOperationException exception)
        {
            // 事件流不可读（恢复失败后的降级房间）：显式失败 + 审计，不让未处理异常抛穿 Hub；
            // 对玩家只说中性原因——"数据丢了"属于说书人视图（票据 room-health-degradation-flag 的边界）。
            _logger.LogError(
                exception,
                "玩家加入失败：房间事件流不可读（等说书人显式重建）：seat={Seat} connection={ConnectionId}",
                seatTicket.Seat,
                Context.ConnectionId);
            throw new HubException("加入暂时失败，请稍后重试或联系说书人");
        }
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
            Commands().SubmitResponse(requestId, optionValue),
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
            Commands().VoidRequest(requestId, reason, note),
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
            Commands().ProxyFill(requestId, optionValue, note),
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
            Commands().ResolveDecisionPoint(decisionPointId, decision, note),
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

        return ExecuteAsync(
            actor,
            Commands().ReportSeatState(seat, life, character, alignment, drunk, poison, reason, causedBySeat),
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
        return ExecuteAsync(actor, Commands().AssignCharacters(assignments), idempotencyKey);
    }

    /// <summary>说书人 / 宿主开启夜晚：服务端按规则表建表（口径是引擎输入，R-0014）。</summary>
    public Task<CommandResultDto> StartNight(
        string credential,
        int nightNumber,
        string variant,
        string idempotencyKey)
    {
        var actor = ResolveActor(credential);
        return ExecuteAsync(actor, Commands().StartNight(nightNumber, variant), idempotencyKey);
    }

    /// <summary>说书人 / 宿主开启白天：天数由服务端按已开始的白天数推导（R-0014 同族的做法）。</summary>
    public Task<CommandResultDto> StartDay(string credential, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new StartDayCommand(), idempotencyKey);

    /// <summary>玩家发起提名（提名者由连接凭据推导，命令面无自称身份）。</summary>
    public Task<CommandResultDto> Nominate(string credential, int nomineeSeat, string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new NominateCommand { Nominee = new SeatId(nomineeSeat) },
            idempotencyKey);

    /// <summary>玩家在当前开放的提名上投票 / 撤回（在线口径见 R-0017）。</summary>
    public Task<CommandResultDto> CastVote(
        string credential,
        int nominationIndex,
        bool voted,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new CastVoteCommand { NominationIndex = nominationIndex, Voted = voted },
            idempotencyKey);

    /// <summary>说书人 / 宿主对当前开放的提名计票（票面快照冻结）。</summary>
    public Task<CommandResultDto> CountVotes(string credential, int nominationIndex, string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new CountVotesCommand { NominationIndex = nominationIndex },
            idempotencyKey);

    /// <summary>说书人 / 宿主结束白天：处决当前「即将被处决」者（如果有），然后关闭白天。</summary>
    public Task<CommandResultDto> CloseDay(string credential, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new CloseDayCommand(), idempotencyKey);

    /// <summary>
    /// 说书人 / 宿主处罚处决：洗脑师 / 畸形秀演员的"疯狂"后果（R-0020）。
    /// 白天形态占用当天处决上限并立即收口白天；夜晚形态不占任何白天的上限。
    /// </summary>
    public Task<CommandResultDto> PunishExecution(
        string credential,
        int seat,
        string source,
        string? note,
        string idempotencyKey)
    {
        var actor = ResolveActor(credential);
        return ExecuteAsync(actor, Commands().PunishExecution(seat, source, note), idempotencyKey);
    }

    /// <summary>
    /// 说书人 / 宿主在麻脸巫婆之夜**追加死亡**：让某名玩家死亡，归因为麻脸巫婆（R-0030 第 4 条）。
    /// 窗口不存在或目标已死时内核显式拒绝。
    /// </summary>
    public Task<CommandResultDto> PitHagCasualty(
        string credential,
        int seat,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().PitHagCasualty(seat, note), idempotencyKey);

    /// <summary>
    /// 说书人 / 宿主**裁定一条待定死亡**：确认（该玩家死亡）或阻止（免死）——麻脸巫婆之夜（R-0030 第 2 条）。
    /// </summary>
    public Task<CommandResultDto> ResolveDeferredDeath(
        string credential,
        int seat,
        bool killed,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().ResolveDeferredDeath(seat, killed, note),
            idempotencyKey);

    /// <summary>说书人 / 宿主按事件日志重建房间（D-0014 恢复）。</summary>
    public Task<CommandResultDto> RebuildRoom(string credential, string reason, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new RebuildRoomCommand { Reason = reason }, idempotencyKey);

    /// <summary>
    /// 说书人 / 宿主给某席加一条自由文本注记（D-0019）。
    /// 文本的归一化与有界化在 Application / Kernel 做（客户端数据不可信，D-0012）；玩家侧没有入口。
    /// </summary>
    public Task<CommandResultDto> AddSeatAnnotation(
        string credential,
        int seat,
        string text,
        string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().AddSeatAnnotation(seat, text), idempotencyKey);

    /// <summary>说书人 / 宿主改一条注记的文本（D-0019）；席位与标识不变。</summary>
    public Task<CommandResultDto> UpdateSeatAnnotation(
        string credential,
        int annotationId,
        string text,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().UpdateSeatAnnotation(annotationId, text),
            idempotencyKey);

    /// <summary>说书人 / 宿主删一条注记（D-0019）：写删除事件，不抹历史。</summary>
    public Task<CommandResultDto> RemoveSeatAnnotation(
        string credential,
        int annotationId,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().RemoveSeatAnnotation(annotationId),
            idempotencyKey);

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

    /// <summary>
    /// 本次调用的命令翻译器（wire 参数 → 应用层命令；参数层拒绝按调用者写审计）。
    /// 凭据闸先过、参数再解析：未认证连接不该用畸形参数触发异常与日志噪声。
    /// </summary>
    private GameCommandFactory Commands([CallerMemberName] string method = "") =>
        new(_logger, Context.ConnectionId, method);
}
