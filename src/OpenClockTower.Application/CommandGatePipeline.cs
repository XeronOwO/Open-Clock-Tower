using OpenClockTower.Kernel;
using static OpenClockTower.Application.GateRejections;

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

            NominateExtraCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            NominateExtraCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以发起额外提名",
                "identity"),

            CastVoteCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            CastVoteCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以投票",
                "identity"),

            // 钟盘收票（R-0017 目标形态）：身份 / 参数形状的闸在 VoteSweepGate。
            StartVoteSweepCommand or ResumeVoteSweepCommand or CollectSeatVoteCommand
                => VoteSweepGate.IdentityRejection(envelope.Command, actor),

            // 流放（票据 traveller-and-exile · D2 / D3）：身份 / 参数形状的闸在 ExileGate。
            ProposeExileCommand or CastExileVoteCommand or StartExileSweepCommand
                or CollectExileSeatVoteCommand or ResumeExileSweepCommand or CountExileVotesCommand
                or ResolveDayProtectionCommand
                => ExileGate.IdentityRejection(envelope.Command, actor),

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

            // 旅行者加入 / 离开（票据 traveller-and-exile D1）：只说书人（或宿主）能发；阶段不限。
            JoinTravellerCommand or RemoveTravellerCommand
                => TravellerGate.IdentityRejection(envelope.Command, actor),

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
                or NominateExtraCommand
                or CastVoteCommand
                or StartVoteSweepCommand
                or CollectSeatVoteCommand
                or ResumeVoteSweepCommand
                or CountVotesCommand
                or ProposeExileCommand
                or CastExileVoteCommand
                or StartExileSweepCommand
                or CollectExileSeatVoteCommand
                or ResumeExileSweepCommand
                or CountExileVotesCommand
                or ResolveDayProtectionCommand
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

            // 旅行者加入 / 离场（D1）：任意时刻都能发生（含首个阶段之前、阶段进行中）——
            // 不能落到下面的 default「machine is null → phase.not_started」。
            case JoinTravellerCommand or RemoveTravellerCommand:
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
            AssignCharactersCommand assign => AssignmentGate.Check(assign, setup),
            StartNightCommand startNight => CheckStartNight(startNight, machine, setup),
            ApplySeatStateCommand seat => SeatGate.CheckExists(seat.Seat, setup),
            NominateCommand nominate => SeatGate.CheckExists(nominate.Nominee, setup),
            NominateExtraCommand nominateExtra => SeatGate.CheckExists(nominateExtra.Nominee, setup),
            CastVoteCommand castVote => CheckNominationIndex(castVote.NominationIndex),
            // 钟盘收票（R-0017 目标形态）：参数范围与席位形状的闸在 VoteSweepGate（与内核同尺）。
            StartVoteSweepCommand or ResumeVoteSweepCommand or CollectSeatVoteCommand
                => VoteSweepGate.LegalityRejection(envelope.Command, setup),
            // 流放（票据 traveller-and-exile · D2 / D3）：参数范围与席位形状的闸在 ExileGate（与内核同尺）。
            ProposeExileCommand or CastExileVoteCommand or StartExileSweepCommand
                or CollectExileSeatVoteCommand or ResumeExileSweepCommand or CountExileVotesCommand
                or ResolveDayProtectionCommand
                => ExileGate.LegalityRejection(envelope.Command, setup),
            CountVotesCommand countVotes => CheckNominationIndex(countVotes.NominationIndex),
            PunishExecutionCommand punish => SeatGate.CheckExists(punish.Seat, setup),
            PitHagCasualtyCommand casualty => SeatGate.CheckExists(casualty.Seat, setup),
            ResolveDeferredDeathCommand deferred => SeatGate.CheckExists(deferred.Seat, setup),
            SubmitResponseCommand submit => CheckOption(machine, submit.RequestId, submit.OptionValue),
            ProxyFillCommand proxy => CheckOption(machine, proxy.RequestId, proxy.OptionValue),
            VoidRequestCommand voidRequest => IsManuallySelectableVoidReason(voidRequest.Reason)
                ? null
                : Reject("legality.reason_invalid", $"不能手动使用的作废原因：{voidRequest.Reason}", "legality"),

            // 说书人注记（D-0019）：席位必须在本局名单里、文本合规、每席不超上限；
            // 改 / 删必须先存在（已删除的标识不再接受）。
            AddSeatAnnotationCommand add => AnnotationGate.CheckAdd(add, setup, annotations),
            UpdateSeatAnnotationCommand update =>
                AnnotationGate.CheckTarget(update.Id, annotations) ?? AnnotationGate.CheckText(update.Text),
            RemoveSeatAnnotationCommand remove => AnnotationGate.CheckTarget(remove.Id, annotations),

            // 旅行者加入 / 离场（D1）：形状检查在这里；"能不能加入 / 离场"读状态账，在内核侧判。
            JoinTravellerCommand join => TravellerGate.LegalityRejection(join, setup),
            RemoveTravellerCommand remove => TravellerGate.LegalityRejection(remove, setup),
            _ => null,
        };

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
}
