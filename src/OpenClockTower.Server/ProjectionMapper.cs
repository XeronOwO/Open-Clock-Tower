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
    };

    /// <summary>胜负结论 → DTO（序号 = 这份结论被表达时的序号）。</summary>
    public static GameOutcomeDto ToDto(GameOutcome outcome, long sequence) => new()
    {
        Sequence = sequence,
        Winner = outcome.Winner.ToString(),
        Condition = outcome.Condition.ToString(),
        Detail = outcome.Detail,
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

    /// <summary>玩家白天投影 → DTO（公开事实 + 公开生死面 + 权限位 + 可提名目标）。</summary>
    public static PlayerDayDto ToDto(PlayerDay day, long sequence) => new()
    {
        Sequence = sequence,
        PublicView = ToDto(day.PublicView),
        Lives = [.. day.Lives.Select(ToDto)],
        Announcements = [.. day.Announcements.Select(ToDto)],
        CanNominate = day.CanNominate,
        CanVote = day.CanVote,
        Voted = day.Voted,
        Candidates = [.. day.NominationCandidates.Select(seat => seat.Value)],
    };

    /// <summary>公开生死面条目 → DTO（席位 + 对外可见生死；不含死因）。</summary>
    public static PlayerLifeDto ToDto(PublicLifeEntry entry) => new()
    {
        Seat = entry.Seat.Value,
        State = entry.State.ToString(),
    };

    /// <summary>白天公开事实 → DTO（最新一天）。</summary>
    public static DayViewDto ToDto(DayRecord day) => new()
    {
        DayNumber = day.DayNumber,
        Status = day.Status.ToString(),
        Nominations = [.. day.Nominations.Select(ToDto)],
        AboutToBeExecuted = day.AboutToBeExecuted?.Value,
        Executed = day.Executed?.Value,
        OpenNominationIndex = day.OpenNomination?.Index,
    };

    /// <summary>
    /// 一次提名 → DTO（票数 = 票面长度；投票中为当前票数，计票后为最终票数）。
    /// </summary>
    /// <remarks>
    /// 投票窗口期内就把票面下发给全体玩家，是 R-0017 第 5 条登记的公开面：
    /// 线下绕圈点数时"谁举了手"所有人都看得见，在线只是把它渲染出来。
    /// 若要改成"计票后才公开票面"，先改裁决条目，再改这里与投影用例。
    /// </remarks>
    public static DayNominationDto ToDto(NominationRecord nomination) => new()
    {
        Index = nomination.Index,
        Nominator = nomination.Nominator.Value,
        Nominee = nomination.Nominee.Value,
        Status = nomination.Status.ToString(),
        Votes = nomination.Ballot.Count,
        Voters = [.. nomination.Ballot.Select(seat => seat.Value)],
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
                Malfunction = resolution.Malfunction?.ToString(),
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
                        Malfunction = ability.Malfunction?.ToString(),
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
        Day = view.Day is { } day ? ToDto(day) : null,
        Outcome = view.Outcome is { } outcome ? ToDto(outcome, view.Sequence) : null,
        KlutzChoices = [.. view.KlutzChoices.Select(record => ToDto(record, view.Sequence))],
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
}
