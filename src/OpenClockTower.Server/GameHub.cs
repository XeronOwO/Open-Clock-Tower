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
/// 本层只做"翻译"：邀请码 / 凭据 → 身份、wire 参数 → 命令、结果 → DTO / 推送；
/// 一切领域判定都在 Application / Kernel（架构 §1：Server 不做领域判断）。
/// 零信任（D-0012 §4.1）：加入时下发**连接级凭据**，此后每条命令的第一个参数都是它；
/// 凭据只在签发它的那条连接上有效，明文不进日志（只记短指纹），客户端声明的身份一律不认。
/// </remarks>
public sealed class GameHub : Hub<IGameClient>
{
    private readonly HubGameScope _scope;
    private readonly HubJoinScope _joinScope;
    private readonly HubTableAdmin _tableAdmin;
    private readonly HubQueryScope _queries;
    private readonly ConnectionRegistry _registry;
    private readonly HubActorResolver _actors;
    private readonly ILogger<GameHub> _logger;

    /// <summary>构造 Hub。</summary>
    /// <param name="scope">连接 ↔ 桌的绑定（多桌：解析本连接在哪一桌，D-0024）。</param>
    /// <param name="joinScope">加入入口（玩家与说书人两侧；自己解析所在桌）。</param>
    /// <param name="tableAdmin">桌务（访问模式 / 解除席位绑定 / 签发邀请码）。</param>
    /// <param name="queries">查询类入口（配板建议 / 复盘页）。</param>
    /// <param name="registry">连接登记表。</param>
    /// <param name="actors">身份解析（凭据 → 操作者）。</param>
    /// <param name="logger">日志。</param>
    public GameHub(
        HubGameScope scope,
        HubJoinScope joinScope,
        HubTableAdmin tableAdmin,
        HubQueryScope queries,
        ConnectionRegistry registry,
        HubActorResolver actors,
        ILogger<GameHub> logger)
    {
        _scope = scope;
        _joinScope = joinScope;
        _tableAdmin = tableAdmin;
        _queries = queries;
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

    /// <summary>玩家凭**邀请码**加入 / 重连（D-0021 / D-0037）：流程本体在 <see cref="HubJoinScope"/>。</summary>
    public Task<SeatJoinDto> JoinByInviteCode(string ticket, string? accountSession, long lastSequence) =>
        _joinScope.JoinByInviteCodeAsync(Clients.Caller, Context.GetHttpContext(), Context.ConnectionId, Context.ConnectionAborted, ticket, accountSession, lastSequence);

    /// <summary>玩家**自助入座**（D-0025 / D-0037）：登录后在公开且未开局的桌挑一个空席位坐下，不要邀请码。</summary>
    public Task<SeatJoinDto> JoinTable(string accountSession, int seat, long lastSequence) =>
        _joinScope.JoinTableAsync(
            Clients.Caller,
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            accountSession,
            seat,
            lastSequence);

    /// <summary>说书人加入（D-0027）：只认这一桌的开桌账号；流程本体在 <see cref="HubJoinFlow"/>。</summary>
    public Task<StorytellerJoinDto> JoinStorytellerWithAccount(string accountSession) =>
        _joinScope.JoinStorytellerWithAccountAsync(
            Context.GetHttpContext(),
            Context.ConnectionId,
            Context.ConnectionAborted,
            accountSession);

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

    /// <summary>
    /// 说书人 / 宿主把一名旅行者移出本局（D1）：席位与票据保留，不再计入任何人数口径（R-0044 第 6 条）。
    /// 说书人**始终保留直接移出**的权限（D-0037）：这一席若有待批的离场申请，会一并结清为"批准"。
    /// </summary>
    public Task<CommandResultDto> RemoveTraveller(string credential, int seat, string? note, string idempotencyKey)
    {
        var actor = ResolveActor(credential);
        return ExecuteAsync(actor, Commands().RemoveTraveller(seat, note), idempotencyKey);
    }

    /// <summary>
    /// **旅行者本人**向说书人提出离场申请（D-0037）：玩家发起，等说书人裁定。
    /// </summary>
    /// <remarks>
    /// 命令面不带席位（由连接凭据推导）；它不是自助离开——批准与执行在
    /// <see cref="ResolveTravellerDeparture"/>，本方法只登记一条待批申请（进事件流、可回放）。
    /// </remarks>
    public Task<CommandResultDto> RequestTravellerDeparture(
        string credential,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().RequestTravellerDeparture(note),
            idempotencyKey);

    /// <summary>
    /// 说书人**裁定**一条离场申请（D-0037）：批准即执行座位离场，驳回则本局继续。
    /// </summary>
    /// <remarks>
    /// 批准与「直接移出」走**同一份**离场合法性判定；权限沿用说书人凭据闸——**本桌**的说书人只能裁本桌的申请。
    /// </remarks>
    public Task<CommandResultDto> ResolveTravellerDeparture(
        string credential,
        int seat,
        bool approved,
        string? note,
        string idempotencyKey) =>
        ExecuteAsync(
            ResolveActor(credential),
            Commands().ResolveTravellerDeparture(seat, approved, note),
            idempotencyKey);

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
        return await _tableAdmin.ReleaseBindingAsync(await GameAsync(), new SeatId(seat), CallerContext.Of(Context.GetHttpContext(), Context.ConnectionId), Context.ConnectionAborted);
    }

    /// <summary>
    /// 桌的访问模式（说书人，D-0025 / D-0037）：`true` = **邀请制**（自助入座被拒、持邀请码者照进）。
    /// </summary>
    /// <remarks>
    /// **切换即时生效并推给该桌所有连接**（说书人 + 在场玩家，不刷新不重连就变）。
    /// 权限沿用说书人凭据闸——**本桌**的说书人只能改本桌。开局之后自助入座本来就被拦（见
    /// <see cref="JoinTable"/>）：那条闸不靠这个开关，也不改写它的值。
    /// </remarks>
    public async Task<bool> SetTableInviteOnly(string credential, bool inviteOnly)
    {
        _ = ResolveStorytellerActor(credential);
        return await _tableAdmin.SetInviteOnlyAsync(await GameAsync(), inviteOnly, CallerContext.Of(Context.GetHttpContext(), Context.ConnectionId), Context.ConnectionAborted);
    }

    /// <summary>
    /// 为某个席位**签发（或轮换）邀请码**（D-0038）：说书人把它转交给玩家，明文只出现这一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 邀请制桌的固定席位、中途到场的旅行者都靠它进门（大厅的座位按钮在这两种桌上点不动）。
    /// 重复调用即**轮换**：旧的那一枚当场失效——码发错人时这就是收场动作。
    /// </para>
    /// <para>
    /// 权限沿用说书人凭据闸：**本桌**的说书人只能给自己这一桌签，别桌的码签不出来。
    /// 明文不进日志（只记短指纹），也不推给任何人——它是说书人一个人的事。
    /// </para>
    /// </remarks>
    public async Task<SeatInvitationDto> IssueSeatInvitation(string credential, int seat)
    {
        _ = ResolveStorytellerActor(credential);
        var game = await GameAsync();
        var issued = await _tableAdmin.IssueInvitationAsync(
            game,
            new SeatId(seat),
            CallerContext.Of(Context.GetHttpContext(), Context.ConnectionId),
            Context.ConnectionAborted);
        return ProjectionMapper.ToDto(game.GameId, issued);
    }

    /// <summary>
    /// 查询开局配板建议（只读、不落账；生成逻辑在 <see cref="HubQueryScope.ProposeSetupAsync"/>）。
    /// </summary>
    /// <param name="nonTravellerCount">
    /// 配板覆盖的非旅行者人数（R-0046：旅行者是叠加角色，不占镇民 / 外来者 / 爪牙 / 恶魔名额）；
    /// null = 本局全部席位都是非旅行者（缺省语义）。
    /// </param>
    public async Task<SetupProposalDto> ProposeSetup(string credential, string? seed, int? nonTravellerCount)
    {
        _ = ResolveStorytellerActor(credential);
        return await HubQueryScope.ProposeSetupAsync(
            await GameAsync(),
            seed,
            nonTravellerCount,
            Context.ConnectionAborted);
    }

    /// <summary>说书人查询当前视图（变更时同时会推送，客户端不需要轮询）。</summary>
    public async Task<StorytellerViewDto> GetStorytellerView(string credential)
    {
        _ = ResolveStorytellerActor(credential);
        return ProjectionMapper.ToDto((await GameAsync()).Session.GetStorytellerView());
    }

    /// <summary>
    /// 查询一页复盘（D-0020 / R-0043）：说书人随时可看，玩家只有本局结束之后才允许
    /// （可见性闸与中性文案在 <see cref="HubQueryScope.GetReplayAsync"/>）。
    /// </summary>
    public async Task<ReplayViewDto> GetReplay(string credential, long afterSequence, int pageSize) =>
        await _queries.GetReplayAsync(
            ResolveActor(credential),
            await GameAsync(),
            afterSequence,
            pageSize,
            Context.ConnectionId,
            Context.ConnectionAborted);

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
