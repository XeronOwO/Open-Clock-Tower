using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 四道闸管线（D-0012 §4.2）：身份 → 幂等 → 阶段 → 合法性，全过才进内核。
/// </summary>
/// <remarks>
/// <para>
/// **顺序说明**：幂等闸紧跟身份闸、放在阶段闸之前——重复投递必须返回首次结果，
/// 而阶段闸会看到"请求已了结"并把重复命令误判成非法；先重放回执才能满足
/// 「同一命令重复投递只生效一次、第二次返回同一结果」。其余顺序与 §4.2 一致。
/// </para>
/// <para>
/// 任何一闸不过：拒绝 + 记录 + 不改状态。拒绝日志由调用方（GameSession）统一写出，
/// 带完整上下文（谁、哪一闸、什么输入）。
/// </para>
/// </remarks>
public static class CommandGatePipeline
{
    /// <summary>按顺序跑四道闸。</summary>
    /// <param name="annotations">
    /// 当前注记账（D-0019）：注记的改 / 删要按它核对存在性与每席上限；其余命令不读。
    /// </param>
    public static GateDecision Evaluate(
        CommandEnvelope envelope,
        StepMachineState? machine,
        CommandReceipt? receipt,
        GameSetup? setup = null,
        SeatAnnotationLedger? annotations = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var ledger = annotations ?? SeatAnnotationLedger.Empty;

        var identity = CheckIdentity(envelope);
        if (identity is not null)
        {
            return GateDecision.Reject(identity);
        }

        if (receipt is not null)
        {
            return GateDecision.Duplicate(receipt);
        }

        // 游戏已经结束：一切新命令都被拒（R-0024）；重复投递仍按上面的回执重放。
        if (machine?.Outcome is { } outcome)
        {
            return GateDecision.Reject(Reject(
                "phase.game_ended",
                $"本局已经结束（{(outcome.Winner == Alignment.Good ? "善良" : "邪恶")}阵营获胜）："
                    + "不能再提交操作",
                "phase"));
        }

        var phase = CheckPhase(envelope, machine);
        if (phase is not null)
        {
            return GateDecision.Reject(phase);
        }

        var legality = CheckLegality(envelope, machine, setup, ledger);
        if (legality is not null)
        {
            return GateDecision.Reject(legality);
        }

        return GateDecision.Pass();
    }

    private static CommandRejection? CheckIdentity(CommandEnvelope envelope)
    {
        var actor = envelope.Actor;
        return envelope.Command switch
        {
            StartPhaseCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartPhaseCommand => Reject(
                "identity.host_only",
                "只有宿主或说书人可以开启新阶段",
                "identity"),

            SubmitResponseCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            SubmitResponseCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以提交响应",
                "identity"),

            SlotQuotaElapsedCommand when actor.Kind == ActorKind.System => null,
            SlotQuotaElapsedCommand => Reject(
                "identity.system_only",
                "配额输入只能由系统节拍器发出",
                "identity"),

            RebuildRoomCommand when actor.Kind is ActorKind.Storyteller or ActorKind.Host => null,
            RebuildRoomCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以重建房间",
                "identity"),

            AssignCharactersCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            AssignCharactersCommand => Reject(
                "identity.host_only",
                "只有宿主或说书人可以开局分配角色",
                "identity"),

            StartNightCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartNightCommand => Reject(
                "identity.host_only",
                "只有宿主或说书人可以开启夜晚",
                "identity"),

            ApplySeatStateCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            ApplySeatStateCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以上报座位状态（上帝视角的观测）",
                "identity"),

            StartDayCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartDayCommand => Reject(
                "identity.host_only",
                "只有宿主或说书人可以开启白天",
                "identity"),

            NominateCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            NominateCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以发起提名",
                "identity"),

            CastVoteCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            CastVoteCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以投票",
                "identity"),

            // 钟盘收票（R-0017 目标形态）：身份 / 参数形状的闸在 VoteSweepGate。
            StartVoteSweepCommand or ResumeVoteSweepCommand or CollectSeatVoteCommand
                => VoteSweepGate.IdentityRejection(envelope.Command, actor),

            AskArtistQuestionCommand => ArtistQuestionGate.IdentityRejection(actor),

            CountVotesCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            CountVotesCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以计票",
                "identity"),

            CloseDayCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            CloseDayCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以结束白天",
                "identity"),

            PunishExecutionCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            PunishExecutionCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以处罚处决",
                "identity"),

            // 麻脸巫婆之夜的两条说书人命令（R-0030）：追加死亡与裁定待定死亡。
            PitHagCasualtyCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            PitHagCasualtyCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以裁定麻脸巫婆之夜的死亡",
                "identity"),

            ResolveDeferredDeathCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            ResolveDeferredDeathCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以裁定待定的死亡",
                "identity"),

            // 说书人注记（D-0019）：只说书人（或宿主）可写，玩家零参与。
            AddSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            AddSeatAnnotationCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以写注记",
                "identity"),

            UpdateSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            UpdateSeatAnnotationCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以改注记",
                "identity"),

            RemoveSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            RemoveSeatAnnotationCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以删注记",
                "identity"),

            _ when actor.Kind == ActorKind.Storyteller => null,
            _ => Reject("identity.storyteller_only", "这条命令只有说书人可以发出", "identity"),
        };
    }

    private static CommandRejection? CheckPhase(CommandEnvelope envelope, StepMachineState? machine)
    {
        // 触发来源的请求（呆瓜死亡选择等）与未结清裁定挂起时，推进类命令一律被拒（白名单见 PendingChoiceGate）。
        if (PendingChoiceGate.TriggerRequestPending(machine, envelope.Command) is { } triggerPending)
        {
            return triggerPending;
        }

        // 触发型裁定点（如心上人死亡触发的说书人选择，R-0039）同族：裁定必须尽快做出（D-0011 / D-0014）。
        if (PendingChoiceGate.TriggerDecisionPending(machine, envelope.Command) is { } triggerDecision)
        {
            return triggerDecision;
        }

        // 艺术家的白天提问（R-0040）同族：白名单与理由见 PendingChoiceGate。
        if (PendingChoiceGate.ArtistQuestionPending(machine, envelope.Command) is { } artistPending)
        {
            return artistPending;
        }

        switch (envelope.Command)
        {
            case SubmitResponseCommand:
                var pending = machine?.PendingRequest;

                // 统一成一条不区分"没有请求 / 已了结 / 不是发给你的"的拒绝：区分它们会让拒绝码本身
                // 成为探测他人活动的预言机（谁在行动、进行到哪一步都是场外信息，D-0013 §5）。
                if (machine is null
                    || pending is null
                    || pending.Status != OperationRequestStatus.Pending
                    || envelope.Actor.Seat != pending.Addressee)
                {
                    return Reject("phase.no_request_for_you", "现在没有等待你响应的请求", "phase");
                }

                return null;

            case SlotQuotaElapsedCommand:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                if (machine.IsPlanCompleted)
                {
                    return Reject("phase.phase_completed", "本阶段已走完", "phase");
                }

                return null;

            case RebuildRoomCommand:
                return null;

            case StartDayCommand:
                if (machine is null)
                {
                    return Reject(
                        "phase.day_requires_night",
                        "白天只能跟在夜晚之后：本局还没有开始过任何阶段",
                        "phase");
                }

                if (!machine.IsPlanCompleted)
                {
                    return Reject(
                        "phase.phase_running",
                        "当前阶段还没有走完；先推进、强推或重建，不要静默丢弃挂起",
                        "phase");
                }

                if (machine.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
                {
                    return Reject(
                        "phase.day_requires_night",
                        "白天只能跟在夜晚之后：上一个阶段不是夜晚",
                        "phase");
                }

                return null;

            // 白天输入统一要求"白天开着"：具体规则（谁有资格、票数够不够、收票到没到点）在内核里判。
            case NominateCommand
                or CastVoteCommand
                or StartVoteSweepCommand
                or CollectSeatVoteCommand
                or ResumeVoteSweepCommand
                or CountVotesCommand
                or CloseDayCommand:
                if (machine is null || machine.Plan.Phase != GamePhase.Day || machine.Day?.OpenDay is null)
                {
                    return Reject("phase.not_open_day", "现在不是白天，或白天已经结束", "phase");
                }

                return null;

            // 艺术家的提问同样只在白天开着时可用（R-0040）；具体规则（是不是艺术家、用没用过）在内核里判。
            case AskArtistQuestionCommand:
                return ArtistQuestionGate.DayRequirement(machine);

            // 处罚处决可在任何已开始的阶段发生（含夜晚、含提名阶段之外：百科《畸形秀演员》；
            // R-0020）：具体依据（要求是否生效 / 是不是畸形秀演员）在内核里判，这里只要求对局已开始。
            case PunishExecutionCommand:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                return null;

            case StartPhaseCommand:
                if (machine is not null && !machine.IsPlanCompleted)
                {
                    return Reject(
                        "phase.phase_running",
                        "当前阶段还没有走完；请先推进、强推或重建，不要静默丢弃挂起",
                        "phase");
                }

                return null;

            case AssignCharactersCommand:
                if (machine is not null)
                {
                    return Reject(
                        "phase.already_started",
                        "角色分配只允许在首个阶段开始前进行（开局设置）",
                        "phase");
                }

                return null;

            case StartNightCommand:
                if (machine is not null && !machine.IsPlanCompleted)
                {
                    return Reject(
                        "phase.phase_running",
                        "当前阶段还没有走完；请先推进、强推或重建，不要静默丢弃挂起",
                        "phase");
                }

                return null;

            case ApplySeatStateCommand:
                // 首个阶段之前也允许状态观测：夜晚建表要求每一席生死都已观测，
                // 而未分配角色的席位只能靠上报补全（否则永远开不了夜）。
                return null;

            // 说书人注记（D-0019）：不参与阶段推进，任何时候都能写（含首个阶段之前）；
            // "本局已结束"由上面的统一拒绝拦下（R-0024）。
            case AddSeatAnnotationCommand or UpdateSeatAnnotationCommand or RemoveSeatAnnotationCommand:
                return null;

            default:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                return null;
        }
    }

    private static CommandRejection? CheckLegality(
        CommandEnvelope envelope,
        StepMachineState? machine,
        GameSetup? setup,
        SeatAnnotationLedger annotations) =>
        envelope.Command switch
        {
            AssignCharactersCommand assign => CheckAssignments(assign, setup),
            StartNightCommand startNight => CheckStartNight(startNight, machine, setup),
            ApplySeatStateCommand seat => CheckSeatExists(seat.Seat, setup),
            NominateCommand nominate => CheckSeatExists(nominate.Nominee, setup),
            CastVoteCommand castVote => CheckNominationIndex(castVote.NominationIndex),
            // 钟盘收票（R-0017 目标形态）：参数范围与席位形状的闸在 VoteSweepGate（与内核同尺）。
            StartVoteSweepCommand or ResumeVoteSweepCommand or CollectSeatVoteCommand
                => VoteSweepGate.LegalityRejection(envelope.Command, setup),
            CountVotesCommand countVotes => CheckNominationIndex(countVotes.NominationIndex),
            PunishExecutionCommand punish => CheckSeatExists(punish.Seat, setup),
            PitHagCasualtyCommand casualty => CheckSeatExists(casualty.Seat, setup),
            ResolveDeferredDeathCommand deferred => CheckSeatExists(deferred.Seat, setup),
            SubmitResponseCommand submit => CheckOption(machine, submit.RequestId, submit.OptionValue),
            ProxyFillCommand proxy => CheckOption(machine, proxy.RequestId, proxy.OptionValue),
            VoidRequestCommand voidRequest => IsManuallySelectableVoidReason(voidRequest.Reason)
                ? null
                : Reject("legality.reason_invalid", $"不能手动使用的作废原因：{voidRequest.Reason}", "legality"),

            // 说书人注记（D-0019）：席位必须在本局名单里、文本合规、每席不超上限；
            // 改 / 删必须先存在（已删除的标识不再接受）。
            AddSeatAnnotationCommand add => CheckAddAnnotation(add, setup, annotations),
            UpdateSeatAnnotationCommand update =>
                CheckAnnotationTarget(update.Id, annotations) ?? CheckAnnotationText(update.Text),
            RemoveSeatAnnotationCommand remove => CheckAnnotationTarget(remove.Id, annotations),
            _ => null,
        };

    /// <summary>状态观测的合法性：席位必须在本局席位名单里（与开局分配同一把尺子）。</summary>
    /// <remarks>
    /// 预阶段与运行期都走这一条：账里写一个不存在的席位，等于让建表读到幽灵数据。
    /// </remarks>
    private static CommandRejection? CheckSeatExists(SeatId seat, GameSetup? setup)
    {
        if (setup is null)
        {
            return Reject("legality.setup_missing", "本局还没有会话信息（席位名单）", "legality");
        }

        return setup.Seats.Any(item => item.Seat == seat)
            ? null
            : Reject("legality.seat_unknown", $"席位 {seat.Value} 不在本局席位名单里", "legality");
    }

    /// <summary>加注记的合法性（D-0019）：席位属于本局、文本合规、每席不超上限。</summary>
    private static CommandRejection? CheckAddAnnotation(
        AddSeatAnnotationCommand command,
        GameSetup? setup,
        SeatAnnotationLedger annotations)
    {
        var seat = CheckSeatExists(command.Seat, setup);
        if (seat is not null)
        {
            return seat;
        }

        var text = CheckAnnotationText(command.Text);
        if (text is not null)
        {
            return text;
        }

        return annotations.CountOn(command.Seat) >= SeatAnnotationText.MaxPerSeat
            ? Reject(
                "legality.annotation_limit",
                $"席位 {command.Seat.Value} 的注记已达上限（每席最多 {SeatAnnotationText.MaxPerSeat} 条）",
                "legality")
            : null;
    }

    /// <summary>改 / 删注记的合法性：注记必须还存在（已删除的标识不再接受）。</summary>
    private static CommandRejection? CheckAnnotationTarget(
        SeatAnnotationId id,
        SeatAnnotationLedger annotations) =>
        annotations.Find(id) is null
            ? Reject("legality.annotation_unknown", $"注记 {id} 不存在（可能已被删除）", "legality")
            : null;

    /// <summary>
    /// 注记文本的合法性（D-0019）：归一化后非空、不超长、不含控制字符。
    /// 归一化本身在分派时做（同一把尺子 <see cref="SeatAnnotationText.TryNormalize"/>）。
    /// </summary>
    private static CommandRejection? CheckAnnotationText(string? raw)
    {
        if (SeatAnnotationText.TryNormalize(raw, out _, out var failure))
        {
            return null;
        }

        return failure switch
        {
            "empty" => Reject("legality.annotation_empty", "注记不能为空", "legality"),
            "too_long" => Reject(
                "legality.annotation_too_long",
                $"注记最多 {SeatAnnotationText.MaxLength} 个字符",
                "legality"),
            _ => Reject("legality.annotation_control", "注记不能包含控制字符", "legality"),
        };
    }

    /// <summary>开局分配的合法性：席位属于本局、角色在首版花名册里、同批不重复（角色唯一）。</summary>
    private static CommandRejection? CheckAssignments(AssignCharactersCommand command, GameSetup? setup)
    {
        if (command.Assignments.Count == 0)
        {
            return Reject("legality.assignment_empty", "开局分配至少要给出一名席位的角色", "legality");
        }

        if (setup is null)
        {
            return Reject("legality.setup_missing", "本局还没有会话信息（席位名单）", "legality");
        }

        var seats = new HashSet<SeatId>();
        var characters = new HashSet<CharacterId>();
        foreach (var assignment in command.Assignments)
        {
            if (!setup.Seats.Any(item => item.Seat == assignment.Seat))
            {
                return Reject(
                    "legality.seat_unknown",
                    $"席位 {assignment.Seat.Value} 不在本局席位名单里",
                    "legality");
            }

            if (!seats.Add(assignment.Seat))
            {
                return Reject(
                    "legality.seat_duplicated",
                    $"同一批分配里席位 {assignment.Seat.Value} 出现了多次",
                    "legality");
            }

            if (!SectsAndVioletsRoster.Contains(assignment.Character))
            {
                return Reject(
                    "legality.character_unknown",
                    $"角色 {assignment.Character.Value} 不是《梦殒春宵》首版角色",
                    "legality");
            }

            if (!characters.Add(assignment.Character))
            {
                return Reject(
                    "legality.character_duplicated",
                    $"同一批分配里角色 {assignment.Character.Value} 出现了多次（角色唯一）",
                    "legality");
            }
        }

        return null;
    }

    /// <summary>开夜的合法性：夜晚序号、口径、会话席位名单（建表完整性由建表器校验）。</summary>
    private static CommandRejection? CheckStartNight(
        StartNightCommand command,
        StepMachineState? machine,
        GameSetup? setup)
    {
        if (command.NightNumber < 1)
        {
            return Reject(
                "legality.night_number_invalid",
                $"夜晚序号必须从 1 开始：{command.NightNumber}",
                "legality");
        }

        if (machine is null && command.NightNumber != 1)
        {
            return Reject(
                "legality.first_night_must_be_one",
                "本局还没有开始过任何阶段：第一夜必须是第 1 夜",
                "legality");
        }

        if (!Enum.IsDefined(command.Variant))
        {
            return Reject("legality.variant_invalid", $"未知的夜晚顺序口径：{command.Variant}", "legality");
        }

        if (setup is null)
        {
            return Reject("legality.setup_missing", "本局还没有会话信息（席位名单）", "legality");
        }

        return null;
    }

    private static CommandRejection? CheckOption(
        StepMachineState? machine,
        OperationRequestId requestId,
        string optionValue)
    {
        var pending = machine?.PendingRequest;
        if (pending is null || pending.Id != requestId)
        {
            return Reject("legality.request_not_current", $"当前挂起的不是 {requestId}", "legality");
        }

        // 两维选择（R-0021）按 `{第一维}|{第二维}` 组合校验；单维仍是精确匹配。
        return pending.Prompt.IsLegalAnswer(optionValue)
            ? null
            : Reject("legality.option_not_legal", $"选项不在合法集合里：{optionValue}", "legality");
    }

    /// <summary>提名序号的形状检查：从 1 开始。是否"当前开放的那一项"由内核按白天账判定。</summary>
    private static CommandRejection? CheckNominationIndex(int index) =>
        index < 1
            ? Reject("legality.nomination_index_invalid", $"提名序号必须从 1 开始：{index}", "legality")
            : null;

    /// <summary>
    /// 说书人可手动选择的作废原因：已登记、且不是系统专属原因。
    /// <see cref="OperationRequestVoidReason.GameEnded"/> 只由结束批次派发（<c>SessionCommit.AppendGameEnding</c>）；
    /// 客户端即使传了也不受理——否则审计里会出现"手动以『本局已结束』为由作废"的假事实。
    /// </summary>
    private static bool IsManuallySelectableVoidReason(OperationRequestVoidReason reason) =>
        Enum.IsDefined(reason) && reason != OperationRequestVoidReason.GameEnded;

    private static CommandRejection Reject(string code, string message, string gate) =>
        new() { Code = code, Message = message, Gate = gate };
}
