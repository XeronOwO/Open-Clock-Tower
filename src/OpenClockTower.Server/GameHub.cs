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
    private readonly HubGameScope _scope;
    private readonly HubJoinScope _joinScope;
    private readonly HubTableAdmin _tableAdmin;
    private readonly ConnectionRegistry _registry;
    private readonly HubActorResolver _actors;
    private readonly ILogger<GameHub> _logger;

    /// <summary>构造 Hub。</summary>
    /// <param name="scope">连接 ↔ 桌的绑定（多桌：解析本连接在哪一桌，D-0024）。</param>
    /// <param name="joinScope">加入入口（玩家与说书人两侧；自己解析所在桌）。</param>
    /// <param name="tableAdmin">桌务（锁桌 / 解除席位绑定）。</param>
    /// <param name="registry">连接登记表。</param>
    /// <param name="actors">身份解析（凭据 → 操作者）。</param>
    /// <param name="logger">日志。</param>
    public GameHub(
        HubGameScope scope,
        HubJoinScope joinScope,
        HubTableAdmin tableAdmin,
        ConnectionRegistry registry,
        HubActorResolver actors,
        ILogger<GameHub> logger)
    {
        _scope = scope;
        _joinScope = joinScope;
        _tableAdmin = tableAdmin;
        _registry = registry;
        _actors = actors;
        _logger = logger;
    }

    /// <summary>本连接所属的桌（解析规则与缓存都在 <see cref="HubGameScope"/>）。</summary>
    private Task<GameInstance> GameAsync() =>
        _scope.GameAsync(Context.GetHttpContext(), Context.ConnectionId, Context.ConnectionAborted);

    /// <summary>本连接的命令执行器（绑定到本连接所在的桌）。</summary>
    private Task<HubCommandExecutor> CommandsAsync() =>
        _scope.CommandsAsync(Context.GetHttpContext(), Context.ConnectionId, Context.ConnectionAborted);

    /// <summary>玩家加入 / 重连（只凭票据的路径，D-0012）：签发连接凭据、返回重连包并**重投**未响应请求。</summary>
    public Task<SeatJoinDto> JoinSeat(string ticket, long lastSequence) =>
        _joinScope.JoinSeatAsync(
            Clients.Caller,
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            ticket,
            lastSequence);

    /// <summary>
    /// 玩家加入 / 重连（带账号会话，D-0021）：票据认领 / 只凭账号回到已认领席位。
    /// </summary>
    /// <remarks>
    /// SignalR **不支持方法重载**（实测会抛 "Duplicate definitions"），所以账号路径单独一个方法名；
    /// 它与 <see cref="JoinSeat"/> 走同一份实现，只有"是否带账号会话"不同。
    /// </remarks>
    public Task<SeatJoinDto> JoinSeatWithAccount(string ticket, string? accountSession, long lastSequence) =>
        _joinScope.JoinSeatWithAccountAsync(
            Clients.Caller,
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            ticket,
            accountSession,
            lastSequence);

    /// <summary>
    /// 玩家**自助入座**（D-0025）：登录后选一个空席位坐下，**不需要任何票据**。
    /// </summary>
    /// <remarks>
    /// 桌由本连接的 <c>?gameId=</c> 决定（与其余命令同源）。说书人票据仍然存在，
    /// 但它只用于"成为说书人"；玩家这一侧从此不必等发票据。
    /// </remarks>
    /// <param name="accountSession">账号会话（必须；游客仍走票据路径）。</param>
    /// <param name="seat">要坐的席位号。</param>
    /// <param name="lastSequence">客户端已见序号（重连补齐用）。</param>
    public Task<SeatJoinDto> JoinTable(string accountSession, int seat, long lastSequence) =>
        _joinScope.JoinTableAsync(
            Clients.Caller,
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            accountSession,
            seat,
            lastSequence);

    /// <summary>
    /// 说书人加入：票据定位身份，签发连接凭据（同局同一时刻只保留一条有效说书人连接）。
    /// </summary>
    /// <remarks>流程本体在 <see cref="HubJoinScope" /> / <see cref="HubJoinFlow" />（单文件 600 行门禁）。</remarks>
    public Task<StorytellerJoinDto> JoinStoryteller(string ticket) =>
        _joinScope.JoinStorytellerAsync(
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            ticket);

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

    /// <summary>玩家（艺术家）在白天向说书人提一个是 / 否问题（R-0040）。</summary>
    public Task<CommandResultDto> AskArtistQuestion(
        string credential,
        string question,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().AskArtistQuestion(question),
            idempotencyKey);

    /// <summary>玩家（博学者）在白天向说书人要两条信息（R-0057）。</summary>
    public Task<CommandResultDto> AskSavantQuestion(
        string credential,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().AskSavantQuestion(),
            idempotencyKey);

    /// <summary>玩家（杂耍艺人）在自己的首个白天公开猜测 0–5 名玩家的角色（R-0057-B）。</summary>
    public Task<CommandResultDto> MakeJugglerGuesses(
        string credential,
        JugglerGuessDto[]? guesses,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().MakeJugglerGuesses(guesses),
            idempotencyKey);

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

    /// <summary>说书人 / 宿主把一名旅行者加入本局（D1）：任意时刻可用（含开局前、阶段中）。</summary>
    /// <param name="seat">目标席位；null = 服务端追加新席位并签发新票据（结果里的 IssuedSeat / IssuedSeatTicket）。</param>
    /// <param name="character">旅行者角色 slug（花名册五选一；Application 层按花名册复核）。</param>
    /// <param name="alignment">说书人私下裁定的阵营（Good / Evil）；不进任何公开投影。</param>
    /// <param name="revealDemonSeats">邪恶旅行者要告知的存活恶魔席位（说书人选一名或全部；善良必须为空）。</param>
    public Task<CommandResultDto> JoinTraveller(string credential, int? seat, string character, string alignment, int[]? revealDemonSeats, string idempotencyKey)
    {
        var actor = ResolveActor(credential);
        return ExecuteAsync(actor, Commands().JoinTraveller(seat, character, alignment, revealDemonSeats), idempotencyKey);
    }

    /// <summary>说书人 / 宿主把一名旅行者移出本局（D1）：席位与票据保留，不再计入任何人数口径（R-0044 第 6 条）。</summary>
    public Task<CommandResultDto> RemoveTraveller(string credential, int seat, string? note, string idempotencyKey)
    {
        var actor = ResolveActor(credential);
        return ExecuteAsync(actor, Commands().RemoveTraveller(seat, note), idempotencyKey);
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

    /// <summary>玩家（屠夫本人）在额外提名窗口里发起提名（R-0050；提名者由凭据推导）。</summary>
    public Task<CommandResultDto> NominateExtra(string credential, int nomineeSeat, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().NominateExtra(nomineeSeat), idempotencyKey);

    /// <summary>玩家在当前开放的提名上举手 / 放下（先举也算、过时不候；R-0017 目标形态）。</summary>
    public Task<CommandResultDto> CastVote(
        string credential,
        int nominationIndex,
        bool voted,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new CastVoteCommand { NominationIndex = nominationIndex, Voted = voted },
            idempotencyKey);

    /// <summary>说书人 / 宿主开始钟盘收票：倒计时 + 分针逐席旋转（R-0017 目标形态）。</summary>
    public Task<CommandResultDto> StartVoteSweep(
        string credential,
        int nominationIndex,
        int countdownMilliseconds,
        int intervalMilliseconds,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new StartVoteSweepCommand
            {
                NominationIndex = nominationIndex,
                CountdownMilliseconds = countdownMilliseconds,
                IntervalMilliseconds = intervalMilliseconds,
            },
            idempotencyKey);

    /// <summary>说书人 / 宿主继续中断的钟盘收票（重新起倒计时，从下一未收席位接着收）。</summary>
    public Task<CommandResultDto> ResumeVoteSweep(string credential, int nominationIndex, string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new ResumeVoteSweepCommand { NominationIndex = nominationIndex },
            idempotencyKey);

    /// <summary>说书人 / 宿主在收票全部完成后计票（票面 = 逐席冻结结论；R-0017 目标形态）。</summary>
    public Task<CommandResultDto> CountVotes(string credential, int nominationIndex, string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            new CountVotesCommand { NominationIndex = nominationIndex },
            idempotencyKey);

    /// <summary>说书人 / 宿主结束白天：处决当前「即将被处决」者（如果有），然后关闭白天。</summary>
    public Task<CommandResultDto> CloseDay(string credential, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), new CloseDayCommand(), idempotencyKey);

    /// <summary>玩家发起流放提议（R-0044 第 2 条：任意在局玩家、含死者；发起人由凭据推导）。</summary>
    public Task<CommandResultDto> ProposeExile(string credential, int targetSeat, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().ProposeExile(targetSeat), idempotencyKey);

    /// <summary>玩家在当前开放的流放提议上举手 / 放下（R-0044 第 4 条）。</summary>
    public Task<CommandResultDto> CastExileVote(
        string credential,
        int exileIndex,
        bool voted,
        string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().CastExileVote(exileIndex, voted), idempotencyKey);

    /// <summary>说书人 / 宿主开始流放收票：倒计时 + 分针逐席旋转（R-0044 第 10 条沿用 R-0017）。</summary>
    public Task<CommandResultDto> StartExileSweep(
        string credential,
        int exileIndex,
        int countdownMilliseconds,
        int intervalMilliseconds,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().StartExileSweep(exileIndex, countdownMilliseconds, intervalMilliseconds),
            idempotencyKey);

    /// <summary>说书人 / 宿主继续中断的流放收票（重新起倒计时，从下一未收席位接着收）。</summary>
    public Task<CommandResultDto> ResumeExileSweep(string credential, int exileIndex, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().ResumeExileSweep(exileIndex), idempotencyKey);

    /// <summary>说书人 / 宿主在流放收票全部完成后计票（R-0044 第 5 / 9 条）。</summary>
    public Task<CommandResultDto> CountExileVotes(string credential, int exileIndex, string idempotencyKey) =>
        ExecuteAsync(ResolveActor(credential), Commands().CountExileVotes(exileIndex), idempotencyKey);

    /// <summary>
    /// 说书人 / 宿主裁定某席位「今天的死亡保护」（R-0048）：只在流放收票已收完、票面达线且尚未裁定时受理。
    /// 怪咖的「今天是否有趣」由这条命令回答（有趣 → 受保护）；受理条件与拒绝码由内核给出。
    /// </summary>
    public Task<CommandResultDto> ResolveDayProtection(
        string credential,
        int seat,
        bool isProtected,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().ResolveDayProtection(seat, isProtected, note),
            idempotencyKey);

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

    /// <summary>说书人 / 宿主解除席位绑定（D-0021：误认领兜底）：清掉「席位 ↔ 账号」并推送新名字。</summary>
    /// <remarks>
    /// 不是游戏命令、不产生事件（绑定是会话信息）；解除后席位回到无名状态，可被其他账号重新认领。
    /// 编排在 <see cref="SeatJoinCoordinator.ReleaseBindingAsync"/>（与加入 / 认领同族）。
    /// </remarks>
    public async Task<bool> ReleaseSeatBinding(string credential, int seat)
    {
        _ = ResolveStorytellerActor(credential);
        return await _tableAdmin.ReleaseBindingAsync(await GameAsync(), new SeatId(seat), Context.ConnectionAborted);
    }

    /// <summary>
    /// 锁桌 / 解锁（说书人，D-0025）：锁定后不再接受新的自助入座，已在座的玩家不受影响。
    /// </summary>
    /// <remarks>
    /// 需要它的理由很直接：玩家能自己进桌之后，说书人必须能在开局前把人挡在门外。
    /// 权限沿用既有的说书人凭据闸——**本桌**的说书人只能锁本桌。
    /// </remarks>
    public async Task<bool> SetTableLock(string credential, bool isLocked)
    {
        _ = ResolveStorytellerActor(credential);
        return await _tableAdmin.SetLockAsync(await GameAsync(), isLocked, Context.ConnectionAborted);
    }

    /// <summary>
    /// 查询开局配板建议（只读、不落账）：按官方分布表 + 在场角色的设置调整生成建议。
    /// 随机只作显式输入——种子可由客户端传入、缺省由服务端生成并回传（R-0041 / R-0042）。
    /// </summary>
    /// <param name="nonTravellerCount">
    /// 配板覆盖的非旅行者人数（R-0046：旅行者是叠加角色，不占镇民 / 外来者 / 爪牙 / 恶魔名额）；
    /// null = 本局全部席位都是非旅行者（缺省语义）。
    /// </param>
    public async Task<SetupProposalDto> ProposeSetup(string credential, string? seed, int? nonTravellerCount)
    {
        _ = ResolveStorytellerActor(credential);
        var result = await (await GameAsync()).Session.ProposeSetupAsync(seed, nonTravellerCount, Context.ConnectionAborted);
        return ProjectionMapper.ToDto(result);
    }

    /// <summary>说书人查询当前视图（变更时同时会推送，客户端不需要轮询）。</summary>
    public async Task<StorytellerViewDto> GetStorytellerView(string credential)
    {
        _ = ResolveStorytellerActor(credential);
        return ProjectionMapper.ToDto((await GameAsync()).Session.GetStorytellerView());
    }

    /// <summary>
    /// 查询一页复盘（D-0020 / R-0043）：说书人随时可看（实时面），玩家只有本局结束之后才允许——
    /// 可见性闸在 Application 强制；Server 只翻译身份与拒绝。
    /// </summary>
    /// <param name="credential">连接级凭据（D-0012）。</param>
    /// <param name="afterSequence">客户端已拿到的最大事件序号；首次传 0。</param>
    /// <param name="pageSize">本页最多返回的步骤数（Application 侧钳制）。</param>
    public async Task<ReplayViewDto> GetReplay(string credential, long afterSequence, int pageSize)
    {
        var actor = ResolveActor(credential);
        try
        {
            var replay = await (await GameAsync()).Replay.ReadAsync(
                actor,
                afterSequence,
                pageSize,
                Context.ConnectionAborted);
            return ProjectionMapper.ToDto(replay);
        }
        catch (ReplayAccessDeniedException exception)
        {
            // 中性文案：只说明什么时候可以看，不泄露任何局面信息（R-0043）。
            _logger.LogInformation(
                "复盘查询被拒：connection={ConnectionId} kind={Kind} 原因={Reason}",
                Context.ConnectionId,
                actor.Kind,
                exception.Message);
            throw new HubException(exception.Message);
        }
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Remove(Context.ConnectionId);
        // 绑定是按连接记的：断线时一并忘掉，避免长跑进程里字典无限增长（多桌 D-0024）。
        _scope.Forget(Context.ConnectionId);
        _logger.LogInformation(
            "连接已断开：connection={ConnectionId} 异常={Exception}",
            Context.ConnectionId,
            exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 执行一条命令（执行机制在 <see cref="HubCommandExecutor"/>，单文件 600 行门禁）；
    /// 这里只把当前连接的取消令牌接上。
    /// </summary>
    private async Task<CommandResultDto> ExecuteAsync(
        Actor actor,
        GameCommand command,
        string idempotencyKey,
        long clientSequence = 0)
    {
        var executor = await CommandsAsync();
        return await executor.ExecuteAsync(actor, command, idempotencyKey, clientSequence, Context.ConnectionAborted);
    }

    /// <summary>
    /// 凭据 → 身份（唯一的身份来源；D-0012：客户端声明一律不认）。
    /// </summary>
    /// <remarks>解析与审计在 <see cref="HubActorResolver"/>（单文件 600 行门禁）；这里只转发当前连接。</remarks>
    private Actor ResolveActor(string? credential, [CallerMemberName] string method = "") =>
        _actors.Resolve(credential, Context.ConnectionId, method);

    /// <summary>查询类入口要求说书人身份（查询不属于命令，不走四道闸）。</summary>
    private Actor ResolveStorytellerActor(string? credential, [CallerMemberName] string method = "") =>
        _actors.ResolveStoryteller(credential, Context.ConnectionId, method);

    /// <summary>
    /// 本次调用的命令翻译器（wire 参数 → 应用层命令；参数层拒绝按调用者写审计）。
    /// 凭据闸先过、参数再解析：未认证连接不该用畸形参数触发异常与日志噪声。
    /// </summary>
    private GameCommandFactory Commands([CallerMemberName] string method = "") =>
        new(_logger, Context.ConnectionId, method);
}
