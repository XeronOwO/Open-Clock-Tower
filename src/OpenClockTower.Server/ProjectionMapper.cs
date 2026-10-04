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
        Options = request.Prompt.Options
            .Select(option => new DecisionOptionDto { Value = option.Value, Preview = option.Preview })
            .ToArray(),
        SecondaryOptions = request.Prompt.SecondaryOptions
            .Select(option => new DecisionOptionDto { Value = option.Value, Preview = option.Preview })
            .ToArray(),
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
        PendingRequest = view.PendingRequest is { } pending ? ToDto(pending, view.Sequence) : null,
        InformationResults = [.. view.InformationResults.Select(ToDto)],
        Day = view.Day is { } day ? ToDto(day, view.Sequence) : null,
        Outcome = view.Outcome is { } outcome ? ToDto(outcome, view.Sequence) : null,
        KlutzChoices = [.. view.KlutzChoices.Select(record => ToDto(record, view.Sequence))],
        SeatNames = [.. view.SeatNames.Select(ToDto)],
        PendingQuestion = view.PendingQuestion,
        CanAskArtistQuestion = view.CanAskArtistQuestion,
        ExhaustedAbilities = [.. view.ExhaustedAbilities],
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

    /// <summary>玩家白天投影 → DTO（公开事实 + 公开生死面 + 权限位 + 可提名目标 + 收票呈现）。</summary>
    public static PlayerDayDto ToDto(PlayerDay day, long sequence) => new()
    {
        Sequence = sequence,
        PublicView = ToDto(day.PublicView, day.VoteSweep),
        Lives = [.. day.Lives.Select(ToDto)],
        Announcements = [.. day.Announcements.Select(ToDto)],
        CanNominate = day.CanNominate,
        CanVote = day.CanVote,
        Voted = day.Voted,
        SeatCollected = day.SeatCollected,
        Candidates = [.. day.NominationCandidates.Select(seat => seat.Value)],
    };

    /// <summary>公开生死面条目 → DTO（席位 + 对外可见生死；不含死因）。</summary>
    public static PlayerLifeDto ToDto(PublicLifeEntry entry) => new()
    {
        Seat = entry.Seat.Value,
        State = entry.State.ToString(),
    };

    /// <summary>白天公开事实 → DTO（最新一天；收票呈现只挂在当前开放的那一项提名上）。</summary>
    public static DayViewDto ToDto(DayRecord day, VoteSweepView? sweep) => new()
    {
        DayNumber = day.DayNumber,
        Status = day.Status.ToString(),
        Nominations = [.. day.Nominations.Select(nomination =>
            ToDto(nomination, nomination.Index == day.OpenNomination?.Index ? sweep : null))],
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
        Sweep = sweep is null
            ? null
            : new DayVoteSweepDto
            {
                Phase = sweep.Phase,
                CurrentSeat = sweep.CurrentSeat,
                Collected = [.. sweep.Collected.Select(seat => seat.Value)],
                CountdownMilliseconds = sweep.CountdownMilliseconds,
                IntervalMilliseconds = sweep.IntervalMilliseconds,
                NextBeatMilliseconds = sweep.NextBeatMilliseconds,
            },
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
            ? [.. decision.Prompt.Options.Select(option => new DecisionOptionDto
            {
                Value = option.Value,
                Preview = option.Preview,
            })]
            : null,
        AwaitingDecisionSeat = view.AwaitingDecisionSeat?.Value,
        BlockedReason = view.BlockedReason,
        CurrentSlotActor = view.CurrentSlotActor?.Value,
        CurrentSlotContext = view.CurrentSlotContext,
        RecentSeatChanges = view.RecentSeatChanges.Select(ToDto).ToArray(),
        Seats = view.Seats.Select(ToDto).ToArray(),
        Effects =
        [
            .. view.PersistentEffects.Select(effect => ToDto(effect)),
            .. view.InstantaneousEffects.Select(effect => ToDto(effect)),
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
                State = digest.State is null ? null : ToDto(digest.State),
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
        Day = view.Day is { } day ? ToDto(day, view.VoteSweep) : null,
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
        Annotations = [.. view.Annotations.Select(ToDto)],
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

    /// <summary>状态账一行 → DTO：只列已观测的维度，未观测的维度不出现。</summary>
    public static SeatStateDto ToDto(SeatStateEntry entry)
    {
        var facts = new List<SeatStateFactDto>(capacity: 5);
        AddFact(facts, "Life", entry.Life);
        AddFact(facts, "Character", entry.Character);
        AddFact(facts, "Alignment", entry.Alignment);
        AddFact(facts, "Drunk", entry.Drunk);
        AddFact(facts, "Poison", entry.Poison);

        return new SeatStateDto
        {
            Seat = entry.Seat.Value,
            Facts = [.. facts],

            // 只下发**未撤下**的要求：实体游戏里标记到期 / 来源失效就移除了（R-0021）；
            // 已撤下的事实留在事件流与审计里，不在牌面上留幽灵标记。
            Madnesses = [.. entry.Madnesses
                .Where(requirement => !requirement.IsTerminated)
                .Select(requirement => requirement.ProveToBe)],
        };
    }

    /// <summary>持续型效果 → DTO（含终止原因；未终止时终止字段为空）。</summary>
    public static EffectDto ToDto(PersistentEffect effect) => new()
    {
        EffectId = effect.Id.Value,
        Kind = "Persistent",
        Ability = effect.Ability.Value,
        Source = effect.Source.Value,
        Target = effect.Target.Value,
        SourceCharacter = effect.SourceCharacter.Value,
        GrantedCharacter = effect.GrantedCharacter?.Value,
        Terminated = effect.IsTerminated,
        TerminationKind = effect.Termination?.Kind.ToString(),
        TerminationReason = effect.Termination?.Reason,
        TerminationCausedBy = effect.Termination?.CausedBy?.Value,
    };

    /// <summary>即时型效果 → DTO（即时型不回滚，因此没有终止字段）。</summary>
    public static EffectDto ToDto(InstantaneousEffect effect) => new()
    {
        EffectId = effect.Id.Value,
        Kind = "Instantaneous",
        Ability = effect.Ability.Value,
        Source = effect.Source.Value,
        Target = effect.Target.Value,
        Terminated = false,
    };

    private static void AddFact<T>(List<SeatStateFactDto> facts, string dimension, StateFact<T>? fact)
        where T : struct
    {
        if (fact is null)
        {
            return;
        }

        facts.Add(new SeatStateFactDto
        {
            Dimension = dimension,
            Value = fact.Value.ToString() ?? string.Empty,
            Reason = fact.Reason,
            CausedBy = fact.CausedBy?.Value,
            EffectId = fact.EffectId?.Value,
        });
    }

    /// <summary>状态变化记录 → DTO。</summary>
    public static SeatChangeDto ToDto(SeatChangeSnapshot change) => new()
    {
        Seat = change.Seat.Value,
        Life = change.Life?.ToString(),
        Character = change.Character?.ToString(),
        Alignment = change.Alignment?.ToString(),
        Drunk = change.Drunk?.ToString(),
        Poison = change.Poison?.ToString(),
        Reason = change.Reason,
        CausedBy = change.CausedBy?.Value,
        EffectId = change.EffectId?.Value,
        Sequence = change.Sequence,
        RecordedAt = change.RecordedAt,
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
