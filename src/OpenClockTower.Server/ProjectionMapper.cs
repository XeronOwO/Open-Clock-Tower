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
    };

    /// <summary>操作请求 → DTO（刻意不带槽位 / 轮次 / 进度）。</summary>
    public static OperationRequestDto ToDto(OperationRequest request) => new()
    {
        RequestId = request.Id.Value,
        Seat = request.Addressee.Value,
        Context = request.Prompt.Context,
        Options = request.Prompt.Options
            .Select(option => new DecisionOptionDto { Value = option.Value, Preview = option.Preview })
            .ToArray(),
    };

    /// <summary>作废内容 → DTO。</summary>
    public static OperationRequestVoidedDto ToDto(OperationRequestId requestId, OperationRequestVoid voided) => new()
    {
        RequestId = requestId.Value,
        Reason = voided.Reason.ToString(),
        Note = voided.Note,
    };

    /// <summary>玩家视图 → DTO。</summary>
    public static PlayerViewDto ToDto(PlayerView view) => new()
    {
        Seat = view.Seat.Value,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        PendingRequest = view.PendingRequest is { } pending ? ToDto(pending) : null,
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
        Request = playerEvent.Request is { } request ? ToDto(request) : null,
        RequestId = playerEvent.RequestId?.Value,
        OptionValue = playerEvent.OptionValue,
        VoidReason = playerEvent.Void?.Reason.ToString(),
        VoidNote = playerEvent.Void?.Note,
    };

    /// <summary>说书人视图 → DTO。</summary>
    public static StorytellerViewDto ToDto(StorytellerView view) => new()
    {
        Sequence = view.Sequence,
        Phase = view.Phase?.ToString() ?? "NotStarted",
        Control = view.Control?.ToString() ?? "NotStarted",
        SlotIndex = view.SlotIndex,
        SlotCount = view.SlotCount,
        CurrentSlotId = view.CurrentSlotId?.Value,
        PlanCompleted = view.PlanCompleted,
        Pending = view.Pending is { } pending
            ? new PendingRequestDto
            {
                Seat = pending.Seat.Value,
                RequestId = pending.RequestId.Value,
                SlotId = pending.SlotId.Value,
                SlotIndex = pending.SlotIndex,
                WaitingSeconds = pending.Waiting?.TotalSeconds,
            }
            : null,
        AwaitingDecisionId = view.AwaitingDecision?.Id.Value,
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
            Madnesses = [.. entry.Madnesses.Select(requirement => requirement.ProveToBe)],
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
        Sequence = change.Sequence,
        RecordedAt = change.RecordedAt,
    };
}
