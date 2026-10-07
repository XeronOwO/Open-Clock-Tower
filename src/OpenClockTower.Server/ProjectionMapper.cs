using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>Application / Kernel → Contracts 的翻译：wire 形状只在这一层出现。</summary>
public static class ProjectionMapper
{
    /// <summary>命令结果 → DTO。</summary>
    public static CommandResultDto ToDto(CommandResult result) => new()
    {
        Kind = result.Kind.ToString(),
        Sequence = result.Sequence,
        RejectionCode = result.Rejection?.Code,
        RejectionMessage = result.Rejection?.Message,
        Failure = result.Failure,
        MachineEquivalent = result.Rebuild?.MachineEquivalent,
        SnapshotEquivalent = result.Rebuild?.SnapshotEquivalent,
        LedgerEquivalent = result.Rebuild?.LedgerEquivalent,
        IssuedSeat = result.IssuedSeat?.Value,
        IssuedSeatTicket = result.IssuedSeatTicket,
    };

    /// <summary>配板建议 → DTO（服务端生成的种子、非旅行者 / 旅行者人数、席位映射、净分布与显式说明）。</summary>
    public static SetupProposalDto ToDto(SetupProposalResult result) => new()
    {
        Ok = result.Ok,
        Seed = result.Seed,
        NonTravellerCount = result.NonTravellerCount,
        TravellerCount = result.TravellerCount,
        Assignments =
        [
            .. result.Assignments.Select(assignment => new SeatCharacterAssignmentDto
            {
                Seat = assignment.Seat.Value,
                Character = assignment.Character.Value,
            }),
        ],
        Distribution =
        [
            .. result.Distribution.Select(item => new SetupTypeCountDto { Type = item.Type, Count = item.Count }),
        ],
        Notes = [.. result.Notes],
        FailureCode = result.FailureCode,
        FailureMessage = result.FailureMessage,
    };

    /// <summary>操作请求 → DTO（刻意不带槽位 / 轮次 / 进度；序号 = 这条状态对应的事件流序号）。</summary>
    public static OperationRequestDto ToDto(OperationRequest request, long sequence) => new()
    {
        Sequence = sequence,
        RequestId = request.Id.Value,
        Seat = request.Addressee.Value,
        Context = request.Prompt.Context,
        Options = [.. request.Prompt.Options.Select(ToDto)],
        SecondaryOptions = [.. request.Prompt.SecondaryOptions.Select(ToDto)],
    };

    /// <summary>
    /// 一个合法选项 → DTO。真值 / 分组 / 事实编码 / 互斥组 / 徽章是信息类候选（博学者 R-0057-C）的
    /// 呈现元数据；普通候选没有它们（null / 空数组），前端只显示、不推算。
    /// </summary>
    public static DecisionOptionDto ToDto(DecisionOption option) => new()
    {
        Value = option.Value,
        Preview = option.Preview,
        Truth = option.Truth?.ToString(),
        Group = option.Group,
        Code = option.Code,
        ExclusionGroup = option.ExclusionGroup,
        Tags = [.. option.Tags],
    };

    /// <summary>作废内容 → DTO。</summary>
    public static OperationRequestVoidedDto ToDto(OperationRequestId requestId, OperationRequestVoid voided, long sequence) => new()
    {
        Sequence = sequence,
        RequestId = requestId.Value,
        Reason = voided.Reason.ToString(),
        Note = voided.Note,
    };

    /// <summary>响应内容 → DTO（请求标识 + 选项 + 来源 + 说明）。</summary>
    public static OperationRequestAnsweredDto ToDto(OperationRequestId requestId, OperationRequestAnswer answer, long sequence) => new()
    {
        Sequence = sequence,
        RequestId = requestId.Value,
        OptionValue = answer.OptionValue,
        Source = answer.Source.ToString(),
        Note = answer.Note,
    };

    /// <summary>阶段 → DTO（公开信息）。</summary>
    public static PhaseStartedDto ToDto(GamePhase phase, long sequence) => new()
    {
        Sequence = sequence,
        Phase = phase.ToString(),
    };

    /// <summary>玩家视图 → DTO。</summary>
    public static PlayerViewDto ToDto(PlayerView view) => new()
    {
        Seat = view.Seat.Value,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        Character = view.Character?.Value,
        Alignment = view.Alignment?.ToString(),
        PendingRequest = view.PendingRequest is { } pending ? ToDto(pending, view.Sequence) : null,
        InformationResults = [.. view.InformationResults.Select(ToDto)],
        Day = view.Day is { } day ? ToDto(day, view.Sequence) : null,
        Outcome = view.Outcome is { } outcome ? ToDto(outcome, view.Sequence) : null,
        KlutzChoices = [.. view.KlutzChoices.Select(record => ToDto(record, view.Sequence))],
        SeatNames = [.. view.SeatNames.Select(ToDto)],
        PendingQuestion = view.PendingQuestion,
        CanAskArtistQuestion = view.CanAskArtistQuestion,
        CanAskSavantQuestion = view.CanAskSavantQuestion,
        AwaitingSavantQuestion = view.AwaitingSavantQuestion,
        ExhaustedAbilities = [.. view.ExhaustedAbilities],
        Departed = view.Departed,
        CanRequestDeparture = view.CanRequestDeparture,
        HasPendingDeparture = view.HasPendingDeparture,
        PendingDepartureNote = view.PendingDepartureNote,
        LastDepartureRuling = view.LastDepartureRuling is { } ruling
            ? new DepartureRulingDto
            {
                Seat = ruling.Seat.Value,
                Approved = ruling.Approved,
                Note = ruling.Note,
                Sequence = ruling.Sequence,
            }
            : null,
    };

    /// <summary>胜负结论 → DTO（序号 = 这份结论被表达时的序号）。</summary>
    public static GameOutcomeDto ToDto(GameOutcome outcome, long sequence) => new()
    {
        Sequence = sequence,
        Winner = outcome.Winner.ToString(),
        Condition = outcome.Condition.ToString(),
        Detail = outcome.Detail,
    };

    /// <summary>席位 → 玩家名 → DTO（公开信息，无序号字段：它随视图整份下发）。</summary>
    public static SeatDisplayNameDto ToDto(SeatDisplayName name) => new()
    {
        Seat = name.Seat.Value,
        DisplayName = name.DisplayName,
    };

    /// <summary>呆瓜选择记录 → DTO（序号 = 这份记录被表达时的序号）。</summary>
    public static KlutzChoiceDto ToDto(KlutzChoiceRecord record, long sequence) => new()
    {
        Sequence = sequence,
        Seat = record.Klutz.Value,
        Target = record.Target?.Value,
        Made = record.IsMade,
        Detail = record.Detail,
    };

    /// <summary>呆瓜选择事件 → DTO（公开广播用；序号 = 背书事件序号）。</summary>
    public static KlutzChoiceDto ToDto(KlutzChoiceMadeEvent choice, long sequence) => new()
    {
        Sequence = sequence,
        Seat = choice.Klutz.Value,
        Target = choice.Target.Value,
        Made = true,
        Detail = $"呆瓜（{choice.Klutz.Value} 号）公开选择了 {choice.Target.Value} 号",
    };

    /// <summary>玩家白天投影 → DTO（公开事实 + 公开生死面 + 权限位 + 可提名 / 可流放目标 + 收票呈现）。</summary>
    public static PlayerDayDto ToDto(PlayerDay day, long sequence) => new()
    {
        Sequence = sequence,
        PublicView = ToDto(day.PublicView, day.VoteSweep, day.ExileSweep),
        Lives = [.. day.Lives.Select(ToDto)],
        Announcements = [.. day.Announcements.Select(ToDto)],
        CanNominate = day.CanNominate,
        CanVote = day.CanVote,
        Voted = day.Voted,
        SeatCollected = day.SeatCollected,
        Candidates = [.. day.NominationCandidates.Select(seat => seat.Value)],
        CanProposeExile = day.CanProposeExile,
        ExileCandidates = [.. day.ExileCandidates.Select(seat => seat.Value)],
        CanVoteExile = day.CanVoteExile,
        ExileVoted = day.ExileVoted,
        ExileSeatCollected = day.ExileSeatCollected,
        CanNominateExtra = day.CanNominateExtra,
        ExtraNominationCandidates = [.. day.ExtraNominationCandidates.Select(seat => seat.Value)],
        CanMakeJugglerGuesses = day.CanMakeJugglerGuesses,
    };

    /// <summary>公开生死面条目 → DTO（席位 + 对外可见生死；不含死因）。</summary>
    public static PlayerLifeDto ToDto(PublicLifeEntry entry) => new()
    {
        Seat = entry.Seat.Value,
        State = entry.State.ToString(),
    };

    /// <summary>白天公开事实 → DTO（最新一天；收票呈现只挂在当前开放的那一项提名 / 流放上）。</summary>
    public static DayViewDto ToDto(DayRecord day, VoteSweepView? nominationSweep, VoteSweepView? exileSweep) => new()
    {
        DayNumber = day.DayNumber,
        Status = day.Status.ToString(),
        Nominations = [.. day.Nominations.Select(nomination =>
            ToDto(nomination, nomination.Index == day.OpenNomination?.Index ? nominationSweep : null))],
        Exiles = [.. day.Exiles.Select(exile =>
            ToDto(exile, exile.Index == day.OpenExile?.Index ? exileSweep : null))],
        OpenExileIndex = day.OpenExile?.Index,
        Protections = [.. day.ProtectionDecisions.Select(ToDto)],
        JugglerGuesses =
        [
            .. day.JugglerGuesses.Select(record => new DayJugglerGuessDto
            {
                Seat = record.Seat.Value,
                Guesses =
                [
                    .. record.Guesses.Select(guess => new JugglerGuessDto
                    {
                        Seat = guess.Seat.Value,
                        Character = guess.Character.Value,
                    }),
                ],
            }),
        ],
        ExtraNomination = day.ExtraNomination is { } window
            ? new DayExtraNominationDto
            {
                Seat = window.Seat.Value,
                Status = window.Status.ToString(),
            }
            : null,
        AboutToBeExecuted = day.AboutToBeExecuted?.Value,
        Executed = day.Executed?.Value,
        OpenNominationIndex = day.OpenNomination?.Index,
    };

    /// <summary>
    /// 一次提名 → DTO：票数 = 已收票的赞成数（计票后为最终票数）；举手与已收票都是公开面
    /// （线下绕圈点数时所有人都看得见，R-0017 第 5 条）。
    /// </summary>
    /// <param name="sweep">钟盘收票的呈现快照；这项提名没在收票时为 null（旧日志 / 已计票项）。</param>
    public static DayNominationDto ToDto(NominationRecord nomination, VoteSweepView? sweep) => new()
    {
        Index = nomination.Index,
        Nominator = nomination.Nominator.Value,
        Nominee = nomination.Nominee.Value,
        Status = nomination.Status.ToString(),
        Votes = nomination.Ballot.Count,
        Voters = [.. nomination.Ballot.Select(seat => seat.Value)],
        HandsRaised = [.. nomination.HandsRaised.Select(seat => seat.Value)],
        Sweep = sweep is null ? null : ToDto(sweep),
    };

    /// <summary>
    /// 一次流放 → DTO：票数 = 已收票的赞成数（计票后为最终票数）；举手与已收票都是公开面
    /// （R-0044 第 10 条）。钟盘收票呈现与提名共用 <see cref="DayVoteSweepDto"/> 形状。
    /// </summary>
    /// <param name="sweep">钟盘收票的呈现快照；这条流放没在收票时为 null（还没点「开始」/ 已计票）。</param>
    public static DayExileDto ToDto(ExileRecord exile, VoteSweepView? sweep) => new()
    {
        Index = exile.Index,
        Proposer = exile.Proposer.Value,
        Target = exile.Target.Value,
        Status = exile.Status.ToString(),
        Votes = exile.Ballot.Count,
        Voters = [.. exile.Ballot.Select(seat => seat.Value)],
        HandsRaised = [.. exile.HandsRaised.Select(seat => seat.Value)],
        Sweep = sweep is null ? null : ToDto(sweep),
        Conclusion = exile.Conclusion?.ToString(),
    };

    /// <summary>钟盘收票呈现 → DTO（提名与流放共用同一形状；R-0017 目标形态）。</summary>
    public static DayVoteSweepDto ToDto(VoteSweepView sweep) => new()
    {
        Phase = sweep.Phase,
        CurrentSeat = sweep.CurrentSeat,
        Collected = [.. sweep.Collected.Select(seat => seat.Value)],
        CountdownMilliseconds = sweep.CountdownMilliseconds,
        IntervalMilliseconds = sweep.IntervalMilliseconds,
        NextBeatMilliseconds = sweep.NextBeatMilliseconds,
    };

    /// <summary>死亡保护裁定 → DTO（R-0048；每席位每天至多一条）。</summary>
    public static DayProtectionDto ToDto(DayProtectionDecision decision) => new()
    {
        Seat = decision.Seat.Value,
        Protected = decision.Protected,
    };

    /// <summary>信息结果投影 → DTO（只有内容；「可能为假」不出去；序号取快照条目自己的事件序号）。</summary>
    public static InformationResultDto ToDto(InformationResultSnapshot result) => new()
    {
        Sequence = result.Sequence,
        Ability = result.Ability.Value,
        Content = result.Content,
    };

    /// <summary>信息类结果事件 → DTO（只有内容；「可能为假」不出去；序号 = 背书事件序号）。</summary>
    public static InformationResultDto ToDto(InformationResultIssuedEvent information, long sequence) => new()
    {
        Sequence = sequence,
        Ability = information.Ability.Value,
        Content = information.Content,
    };

    /// <summary>重连包 → DTO。</summary>
    public static ReconnectBundleDto ToDto(ReconnectBundle bundle) => new()
    {
        Sequence = bundle.Sequence,
        View = ToDto(bundle.View),
        Events = bundle.EventsSince.Select(ToDto).ToArray(),
    };

    /// <summary>玩家可见事件 → DTO。</summary>
    public static PlayerEventDto ToDto(PlayerEvent playerEvent) => new()
    {
        Sequence = playerEvent.Sequence,
        Kind = playerEvent.Kind.ToString(),
        Phase = playerEvent.Phase?.ToString(),
        Request = playerEvent.Request is { } request ? ToDto(request, playerEvent.Sequence) : null,
        RequestId = playerEvent.RequestId?.Value,
        OptionValue = playerEvent.OptionValue,
        VoidReason = playerEvent.Void?.Reason.ToString(),
        VoidNote = playerEvent.Void?.Note,
        Information = playerEvent.Information is { } information ? ToDto(information, playerEvent.Sequence) : null,
        Seat = playerEvent.Seat?.Value,
        Character = playerEvent.Character?.Value,
    };

    /// <summary>说书人视图 → DTO。</summary>
    public static StorytellerViewDto ToDto(StorytellerView view) => new()
    {
        Sequence = view.Sequence,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        Control = view.Control?.ToString() ?? "NotStarted",
        Health = ToDto(view.Health),
        SlotIndex = view.SlotIndex,
        SlotCount = view.SlotCount,
        CurrentSlotId = view.CurrentSlotId?.Value,
        PlanCompleted = view.PlanCompleted,
        Pending = view.Pending is { } pending
            ? new PendingRequestDto
            {
                Seat = pending.Seat.Value,
                RequestId = pending.RequestId.Value,
                SlotId = pending.SlotId?.Value,
                SlotIndex = pending.SlotIndex,
                TriggerReason = pending.TriggerReason,
                WaitingSeconds = pending.Waiting?.TotalSeconds,
            }
            : null,
        AwaitingDecisionId = view.AwaitingDecision?.Id.Value,
        AwaitingDecisionContext = view.AwaitingDecision?.Prompt.Context,
        AwaitingDecisionOptions = view.AwaitingDecision is { } decision
            ? [.. decision.Prompt.Options.Select(ToDto)]
            : null,
        AwaitingDecisionTruthRule = view.AwaitingDecision is { } truthDecision
            && truthDecision.Prompt.TruthRule != TruthCombinationRule.Unspecified
                ? truthDecision.Prompt.TruthRule.ToString()
                : null,
        AwaitingDecisionTruthNote = view.AwaitingDecision?.Prompt.TruthNote,
        AwaitingDecisionSeat = view.AwaitingDecisionSeat?.Value,
        BlockedReason = view.BlockedReason,
        CurrentSlotActor = view.CurrentSlotActor?.Value,
        CurrentSlotContext = view.CurrentSlotContext,
        RecentSeatChanges = view.RecentSeatChanges.Select(SeatLedgerProjectionMapper.ToDto).ToArray(),
        Seats = view.Seats.Select(SeatLedgerProjectionMapper.ToDto).ToArray(),
        Effects =
        [
            .. view.PersistentEffects.Select(SeatLedgerProjectionMapper.ToDto),
            .. view.InstantaneousEffects.Select(SeatLedgerProjectionMapper.ToDto),
        ],
        AbilityUses =
        [
            .. view.AbilityUses.Select(use => new AbilityUseDto
            {
                Seat = use.Seat.Value,
                Ability = use.Ability.Value,
                Effective = use.Effective,
            }),
        ],
        Malfunctions =
        [
            .. view.Malfunctions.Select(malfunction => new MalfunctionDto
            {
                Seat = malfunction.Seat.Value,
                Ability = malfunction.Ability.Value,
                Kind = malfunction.Kind.ToString(),
            }),
        ],
        LastResolution = view.LastResolution is { } resolution
            ? new AbilityResolutionDto
            {
                Seat = resolution.Actor.Value,
                Ability = resolution.Ability.Value,
                Effective = resolution.Effective,
                Malfunctions = [.. resolution.Malfunctions.Select(kind => kind.ToString())],
                Note = resolution.Note,
                Sequence = resolution.Sequence,
            }
            : null,
        StepDigest = view.StepDigest is { } digest
            ? new StepDigestDto
            {
                Seat = digest.Seat.Value,
                Character = digest.Character?.Value,
                State = digest.State is null ? null : SeatLedgerProjectionMapper.ToDto(digest.State),
                Ability = digest.Ability is { } ability
                    ? new SlotAbilityDto
                    {
                        Basis = ability.Basis.ToString(),
                        Ability = ability.Ability?.Value,
                        Effective = ability.Effective,
                        Malfunctions = [.. ability.Malfunctions.Select(kind => kind.ToString())],
                        Note = ability.Note,
                        Sequence = ability.Sequence,
                    }
                    : null,
                OptionCount = digest.OptionCount,
                OnNoOption = digest.OnNoOption?.ToString(),
            }
            : null,
        LastVoidedRequest = view.LastVoidedRequest is { } voided
            ? new OperationRequestVoidedDto
            {
                Sequence = voided.Sequence,
                RequestId = voided.Id.Value,
                Reason = voided.Reason.ToString(),
                Note = voided.Note,
            }
            : null,
        Day = view.Day is { } day ? ToDto(day, view.VoteSweep, view.ExileSweep) : null,
        Outcome = view.Outcome is { } outcome ? ToDto(outcome, view.Sequence) : null,
        KlutzChoices = [.. view.KlutzChoices.Select(record => ToDto(record, view.Sequence))],
        SeatNames = [.. view.SeatNames.Select(ToDto)],
        PitHagNight = view.PitHagNight is { } night
            ? new PitHagNightDto
            {
                Source = night.Source.Value,
                ClosesAfterSlotIndex = night.ClosesAfterSlotIndex,
                Deferred =
                [
                    .. night.Deferred.Select(deferred => new DeferredDeathDto
                    {
                        Target = deferred.Target.Value,
                        Source = deferred.Source.Value,
                        Ability = deferred.Ability.Value,
                        Note = deferred.Note,
                        Transformation = deferred.Transformation is not null,
                    }),
                ],
            }
            : null,

        // 方古「限一次」/「今晚理发」：整局 / 跨阶段事实只说书人可见（R-0034 / R-0033）。
        FangGuInfection = view.FangGuInfection is { } infection
            ? new FangGuInfectionDto
            {
                Seat = infection.Seat.Value,
                Source = infection.Source.Value,
                Note = infection.Note,
            }
            : null,
        BarberNight = view.BarberNight is { } barber
            ? new BarberNightDto
            {
                Source = barber.Source.Value,
                Note = barber.Note,
            }
            : null,
        // 死亡保护裁定提示（R-0048）：只在「这一席此刻真能被裁定」时非 null；玩家投影里没有它。
        PendingProtection = view.PendingProtection is { } protectionPrompt
            ? new DayProtectionPromptDto
            {
                Seat = protectionPrompt.Seat.Value,
                Outcome = protectionPrompt.Outcome.ToString(),
                Note = protectionPrompt.Note,
            }
            : null,
        Annotations = [.. view.Annotations.Select(ToDto)],
        DepartureRequests =
        [
            .. view.DepartureRequests.Select(request => new DepartureRequestDto
            {
                Seat = request.Seat.Value,
                Note = request.Note,
            }),
        ],
        LostAbilityMarkers =
        [
            .. view.LostAbilityMarkers.Select(marker => new LostAbilityMarkerDto
            {
                Seat = marker.Seat.Value,
                Ability = marker.Ability.Value,
                Note = marker.Note,
            }),
        ],
    };

    /// <summary>说书人注记 → DTO（D-0019）：只说书人视图下发，玩家投影里没有它。</summary>
    public static SeatAnnotationDto ToDto(SeatAnnotation annotation) => new()
    {
        Id = annotation.Id.Value,
        Seat = annotation.Seat.Value,
        Text = annotation.Text,
    };

    /// <summary>房间健康位 → DTO。</summary>
    public static RoomHealthDto ToDto(RoomHealth health) => new()
    {
        Degraded = health.IsDegraded,
        Reason = health.Reason,
        Since = health.Since,
    };

    /// <summary>复盘视图 → DTO（一页步骤；文案与标记都是服务端口径，前端只呈现）。</summary>
    public static ReplayViewDto ToDto(ReplayView view) => new()
    {
        Sequence = view.Sequence,
        Ended = view.Ended,
        HasMore = view.HasMore,
        Steps = [.. view.Steps.Select(step => ToDto(step))],
        SeatNames = [.. view.SeatNames.Select(ToDto)],
    };

    /// <summary>复盘步骤 → DTO。</summary>
    public static ReplayStepDto ToDto(ReplayStep step) => new()
    {
        Sequence = step.Sequence,
        Kind = step.Kind.ToString(),
        Phase = step.Phase?.ToString(),
        Summary = step.Summary,
        Detail = step.Detail,
        Seats = [.. step.Seats.Select(delta => ToDto(delta))],
        Markers = [.. step.Markers.Select(marker => ToDto(marker))],
    };

    /// <summary>复盘席位增量 → DTO。</summary>
    public static ReplaySeatDeltaDto ToDto(ReplaySeatDelta delta) => new()
    {
        Seat = delta.Seat.Value,
        Life = delta.Life?.ToString(),
        Character = delta.Character?.Value,
        PreviousCharacter = delta.PreviousCharacter?.Value,
        Alignment = delta.Alignment?.ToString(),
        Drunk = delta.Drunk?.ToString(),
        Poison = delta.Poison?.ToString(),
        Reason = delta.Reason,
        CausedBy = delta.CausedBy?.Value,
    };

    /// <summary>复盘标记 → DTO（kind 直接透传术语表 slug）。</summary>
    public static ReplayMarkerDto ToDto(ReplayMarker marker) => new()
    {
        Kind = marker.Kind,
        Seat = marker.Seat?.Value,
        From = marker.From?.Value,
        To = marker.To?.Value,
        Text = marker.Text,
    };
}
