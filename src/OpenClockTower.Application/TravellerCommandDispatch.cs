using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离开 / 离场申请的内核产出（票据 `traveller-and-exile` D1 + 本批 D-0037）：
/// 读账的状态相关校验 + 事件。
/// </summary>
/// <remarks>
/// <para>
/// 分派本身无副作用（只有日志）：加入产出「六维度账事件 + 加入事实 +（邪恶）私密揭示」，
/// 离开产出「离场事实」；席位追加与持久化在 <see cref="GameSession"/> 编排（邀请码另行签发，D-0038）。
/// 四条命令都允许在任意时刻发生，因此**必须原样透传步骤机状态**（不能把进行中的阶段抹掉）。
/// </para>
/// <para>
/// 离场申请（D-0037）是"玩家发起 → 说书人裁定"：申请只登记一条待批事实（进状态账的待批表），
/// 裁定批准时才走与直接移出**同一份**离场判定（<see cref="ValidateDeparture"/>）——两条路径共用
/// 一个判据，不各写一份。
/// </para>
/// </remarks>
internal static class TravellerCommandDispatch
{
    private const string JoinReason = "traveller.joined";

    /// <summary>分派一条旅行者命令。</summary>
    internal static CommandDispatchResult Dispatch(
        GameCommand command,
        Actor actor,
        StepMachineState? machine,
        GameSetup setup,
        GameState state,
        GameId gameId,
        ILogger logger) =>
        command switch
        {
            JoinTravellerCommand join => Join(join, machine, setup, state, gameId, logger),
            RemoveTravellerCommand remove => Remove(remove, machine, state, gameId, logger),
            RequestTravellerDepartureCommand request => Request(request, actor, machine, state, gameId, logger),
            ResolveTravellerDepartureCommand resolve => Resolve(resolve, machine, state, gameId, logger),
            _ => CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "kernel.unsupported",
                Message = $"未支持的旅行者命令：{command.GetType().Name}",
                Gate = "kernel",
            }),
        };


    private static CommandDispatchResult Join(
        JoinTravellerCommand join,
        StepMachineState? machine,
        GameSetup setup,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (join.Seat is not { } seat)
        {
            // GameSession 应已把"未指定席位"解析成服务端追加的新席位（含票据）；
            // 到这里还是 null 说明接线漏了，显式拒绝而不是猜一个席位。
            return Reject(
                "legality.traveller_seat_unresolved",
                "旅行者加入的席位还没有解析（服务端分配席位的路径没有生效）");
        }

        if (state.HasDeparted(seat))
        {
            return Reject("legality.seat_departed", $"席位 {seat.Value} 已经离场：不能再作为加入落点");
        }

        if (state.Seat(seat)?.CharacterValue is not null)
        {
            return Reject("legality.seat_occupied", $"席位 {seat.Value} 已经有角色：加入只能落在尚未分配的席位");
        }

        if (state.Seats.FirstOrDefault(entry => entry.CharacterValue == join.Character) is { } holder)
        {
            return Reject(
                "legality.character_duplicated",
                $"角色 {join.Character.Value} 已经属于席位 {holder.Seat.Value}；角色唯一");
        }

        if (join.Alignment == Alignment.Evil && CheckReveal(join, setup, state) is { } revealFailure)
        {
            return CommandDispatchResult.Rejected(revealFailure);
        }

        var events = new List<GameEvent>
        {
            // 六维度初始条件（存活 / 清醒 / 健康）：与开局分配同一条口径（R-0015 / R-0016），
            // 阵营由说书人私下给出，不从角色类型推导（旅行者没有"类型对应阵营"）。
            new SeatStateChangedEvent
            {
                Seat = seat,
                Character = join.Character,
                Alignment = join.Alignment,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = JoinReason,
            },
            new TravellerJoinedEvent
            {
                Seat = seat,
                Character = join.Character,
                Alignment = join.Alignment,
            },
        };

        if (join.Alignment == Alignment.Evil)
        {
            // 私密信息面：只有这名旅行者看得到（D-0012 §4.3）；内容由说书人选定的席位拼出，
            // 不是能力判定，因此 MayBeFalse = false。
            events.Add(new InformationResultIssuedEvent
            {
                Recipient = seat,
                Ability = TravellerJoinInfo.EvilRevealAbility,
                Content = TravellerJoinInfo.ComposeEvilReveal(join.RevealDemonSeats),
                MayBeFalse = false,
                Note = "邪恶旅行者加入：按说书人指定告知存活恶魔"
                    + "（百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式第 2 步）",
            });
        }

        logger.LogInformation(
            "旅行者已加入：game={GameId} seat={Seat} character={Character} alignment={Alignment} "
                + "公开宣告=角色+能力（阵营不公开） 告知恶魔={RevealCount}",
            gameId,
            seat.Value,
            join.Character.Value,
            join.Alignment,
            join.RevealDemonSeats.Count);

        return new CommandDispatchResult(machine, events, null);
    }

    /// <summary>邪恶旅行者的告知目标必须是**在局的存活恶魔**（百科《旅行者》· 旅行者运作方式第 2 步）。</summary>
    private static CommandRejection? CheckReveal(JoinTravellerCommand join, GameSetup setup, GameState state)
    {
        var inGame = InGameSeats.Derive(setup, state);
        foreach (var seat in join.RevealDemonSeats)
        {
            if (!inGame.Contains(seat))
            {
                return LegalityReject("legality.traveller_reveal_invalid", $"告知目标 {seat.Value} 号不在在局座次里");
            }

            var entry = state.Seat(seat);
            if (entry?.LifeValue != LifeState.Alive
                || entry.CharacterValue is not { } character
                || SectsAndVioletsRoster.TypeOf(character) != CharacterType.Demon)
            {
                return LegalityReject(
                    "legality.traveller_reveal_invalid",
                    $"告知目标 {seat.Value} 号不是存活的恶魔：只能告知存活恶魔（百科《旅行者》）");
            }
        }

        return null;
    }

    private static CommandDispatchResult Remove(
        RemoveTravellerCommand remove,
        StepMachineState? machine,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (ValidateDeparture(remove.Seat, machine, state) is { } failure)
        {
            return CommandDispatchResult.Rejected(failure);
        }

        var character = state.Seat(remove.Seat)!.CharacterValue!;
        var events = new List<GameEvent>();

        // 直接移出时顺手结清同一席位待批的离场申请（D-0037）：留着它，复盘里就会出现
        // "人已经走了、申请还挂着"的自相矛盾的账（折叠侧对此显式失败）。
        if (state.DepartureRequestOf(remove.Seat) is not null)
        {
            events.Add(new TravellerDepartureResolvedEvent
            {
                Seat = remove.Seat,
                Approved = true,
                Note = "说书人直接移出（未走申请流程）",
            });
        }

        events.Add(new TravellerDepartedEvent { Seat = remove.Seat, Note = remove.Note });

        logger.LogInformation(
            "旅行者已离场：game={GameId} seat={Seat} character={Character} 说明={Note} 走申请流程={ThroughRequest}",
            gameId,
            remove.Seat.Value,
            character.Value,
            remove.Note,
            state.DepartureRequestOf(remove.Seat) is not null);

        return new CommandDispatchResult(machine, events, null);
    }

    /// <summary>
    /// 旅行者提出离场申请（D-0037）：席位由**连接凭据**给出（<paramref name="actor"/>），
    /// 只登记一条待批事实，不改变任何席位状态。
    /// </summary>
    /// <remarks>
    /// 三条受理条件：该席位在局（未离场）、它持有的角色是旅行者、以及**没有别的申请在等**
    /// （同一席位同时只能有一条待批申请——重复申请只会让说书人看到两条一样的条目）。
    /// </remarks>
    private static CommandDispatchResult Request(
        RequestTravellerDepartureCommand request,
        Actor actor,
        StepMachineState? machine,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (actor.Seat is not { } seat)
        {
            // 身份闸已要求 Player + Seat；到这里还没有说明接线漏了，显式拒绝而不是猜一席。
            return Reject("legality.departure_actor_seat_unknown", "离场申请必须来自一个已入座的玩家");
        }

        if (state.HasDeparted(seat))
        {
            return Reject("legality.seat_departed", $"席位 {seat.Value} 已经离场：没有可再申请的离场");
        }

        if (state.DepartureRequestOf(seat) is { } open)
        {
            return Reject(
                "legality.departure_already_requested",
                $"席位 {seat.Value} 已经有一条待批的离场申请（{open.Note ?? "无说明"}）：等说书人裁定，不要重复提交");
        }

        if (state.Seat(seat)?.CharacterValue is not { } character)
        {
            return Reject(
                "legality.traveller_not_joined",
                $"席位 {seat.Value} 还没有角色：离场流程只适用于已经在局的旅行者（D-0022 范围）");
        }

        if (SectsAndVioletsRoster.TypeOf(character) != CharacterType.Traveller)
        {
            return Reject(
                "legality.not_a_traveller",
                $"席位 {seat.Value} 的角色 {character.Value} 不是旅行者：离场流程只适用于旅行者（D-0022 范围）");
        }

        logger.LogInformation(
            "旅行者提出离场申请（等说书人裁定）：game={GameId} seat={Seat} character={Character} 说明={Note}",
            gameId,
            seat.Value,
            character.Value,
            request.Note);

        return new CommandDispatchResult(
            machine,
            [new TravellerDepartureRequestedEvent { Seat = seat, Note = request.Note }],
            null);
    }

    /// <summary>
    /// 说书人裁定一条离场申请（D-0037）：批准 → 结清申请 + 离场；驳回 → 只结清申请。
    /// </summary>
    /// <remarks>
    /// 批准路径**必须先写结清事件再写离场事件**：折叠侧对"有待批申请却离场"显式失败
    /// （顺序有语义，与麻脸巫婆之夜「追加死亡」的排序同一姿态）。
    /// </remarks>
    private static CommandDispatchResult Resolve(
        ResolveTravellerDepartureCommand resolve,
        StepMachineState? machine,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (state.DepartureRequestOf(resolve.Seat) is not { } pending)
        {
            return Reject(
                "legality.departure_not_requested",
                $"席位 {resolve.Seat.Value} 没有待批的离场申请：没有可裁定的东西");
        }

        if (!resolve.Approved)
        {
            logger.LogInformation(
                "离场申请被驳回（席位留在本局）：game={GameId} seat={Seat} 旅行者说明={RequestNote} 说书人说明={Note}",
                gameId,
                resolve.Seat.Value,
                pending.Note,
                resolve.Note);

            return new CommandDispatchResult(
                machine,
                [new TravellerDepartureResolvedEvent { Seat = resolve.Seat, Approved = false, Note = resolve.Note }],
                null);
        }

        // 批准与直接移出走**同一份**判定（人可能在这期间已经离场 / 流放已经挂上）。
        if (ValidateDeparture(resolve.Seat, machine, state) is { } failure)
        {
            return CommandDispatchResult.Rejected(failure);
        }

        logger.LogInformation(
            "离场申请已批准：game={GameId} seat={Seat} character={Character} 旅行者说明={RequestNote} 说书人说明={Note}",
            gameId,
            resolve.Seat.Value,
            state.Seat(resolve.Seat)!.CharacterValue!.Value,
            pending.Note,
            resolve.Note);

        return new CommandDispatchResult(
            machine,
            [
                new TravellerDepartureResolvedEvent { Seat = resolve.Seat, Approved = true, Note = resolve.Note },
                new TravellerDepartedEvent { Seat = resolve.Seat, Note = resolve.Note },
            ],
            null);
    }

    /// <summary>
    /// 一条离场能不能执行（直接移出与"批准申请"共用这一份判据）。
    /// </summary>
    /// <returns>不能执行时给出拒绝；可以执行时返回 null。</returns>
    /// <remarks>
    /// 依据：百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式（离开 = 移除角色与生命标记）；
    /// 口径见 `rulings.md` R-0044 第 6 条。流放 / 钟盘两条拒绝来自票据「D2 实施口径」：
    /// 不能把人从钟盘下拉走，否则会出现「目标已离场却流放成立」这种自相矛盾的账。
    /// </remarks>
    private static CommandRejection? ValidateDeparture(SeatId seat, StepMachineState? machine, GameState state)
    {
        if (state.HasDeparted(seat))
        {
            return LegalityReject("legality.seat_departed", $"席位 {seat.Value} 已经离场");
        }

        var entry = state.Seat(seat);
        if (entry?.CharacterValue is not { } character)
        {
            return LegalityReject(
                "legality.traveller_not_joined",
                $"席位 {seat.Value} 还没有加入任何旅行者：没有可移除的角色与生命标记");
        }

        if (SectsAndVioletsRoster.TypeOf(character) != CharacterType.Traveller)
        {
            return LegalityReject(
                "legality.not_a_traveller",
                $"席位 {seat.Value} 的角色 {character.Value} 不是旅行者：离场流程只适用于旅行者（D-0022 范围）");
        }

        if (machine?.Day?.OpenDay is { } day)
        {
            if (day.OpenExile is { } openExile && openExile.Target == seat)
            {
                return LegalityReject(
                    "legality.traveller_exile_unsettled",
                    $"席位 {seat.Value} 正在流放流程里（第 {openExile.Index} 条未结清）：先结清流放，再移出旅行者");
            }

            if (day.ActiveBallot is { } active && active.Sweep.Seats.Contains(seat))
            {
                return LegalityReject(
                    "legality.traveller_on_the_dial",
                    $"钟盘收票还在走（{active.Describe()}）：先把它收完并计票，再移出席位 {seat.Value}");
            }
        }

        return null;
    }

    private static CommandDispatchResult Reject(string code, string message) =>
        CommandDispatchResult.Rejected(LegalityReject(code, message));

    private static CommandRejection LegalityReject(string code, string message) =>
        new() { Code = code, Message = message, Gate = "legality" };
}
