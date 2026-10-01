namespace OpenClockTower.Kernel;

/// <summary>
/// 状态账的迁移：把事件折叠成 <see cref="GameState"/>，并让持续型效果跟随来源状态变化。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="StepMachine.Apply"/> 同一套路数（D-0010）：事件是唯一事实来源，状态是折叠结果；
/// 同一条事件流重放必得同一个账。**纯计算**（D-0008）——没有时间、随机、IO，
/// 也没有字典枚举顺序依赖（席位按号排序，效果按发生顺序）。
/// </para>
/// <para>
/// 与状态账无关的事件在下面逐个**显式**列出并原样返回；不认识的类型一律抛错——
/// 将来新增了会改变状态的事件却忘了接进账里，必须在这里当场炸掉，而不是悄悄少记一笔。
/// </para>
/// <para>
/// 顺序损坏（终止不存在的效果、重复终止、重复施加同一个持续型效果、空的状态变化）
/// 一律**显式抛 <see cref="InvalidOperationException"/>**：恢复必须失败，不得静默继续（D-0014 能力 3）。
/// </para>
/// </remarks>
public static class GameStateMachine
{
    /// <summary>从零开始折叠整条事件流；空流得到空账（尚未观测到任何东西是合法状态）。</summary>
    public static GameState Fold(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var state = GameState.Empty;
        foreach (var gameEvent in events)
        {
            state = Apply(state, gameEvent);
        }

        return state;
    }

    /// <summary>把一条事件折叠进状态账。</summary>
    /// <exception cref="InvalidOperationException">事件流顺序损坏时抛出，绝不静默继续。</exception>
    public static GameState Apply(GameState? state, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        var current = state ?? GameState.Empty;

        return gameEvent switch
        {
            SeatStateChangedEvent changed => ApplySeatStateChanged(current, changed),
            PersistentEffectAppliedEvent applied => ApplyPersistentEffectApplied(current, applied),
            PersistentEffectTerminatedEvent terminated => ApplyPersistentEffectTerminated(current, terminated),
            InstantaneousEffectAppliedEvent applied => current with
            {
                InstantaneousEffects = [.. current.InstantaneousEffects, applied.Effect],
            },
            MadnessRequirementIssuedEvent issued => ApplyMadnessRequirementIssued(current, issued),

            // 与状态账无关的事件：步骤机推进、请求生命周期、控制模式、裁定点。
            // 它们照样进事件流，只是不改账里的六维度与效果。
            PhaseStartedEvent => current,
            SlotEnteredEvent => current,
            SlotQuotaElapsedEvent => current,
            SlotAdvancedEvent => current,
            SlotForceAdvancedEvent => current,
            PhaseCompletedEvent => current,
            ControlModeChangedEvent => current,
            PromptSkippedEvent => current,
            DecisionPointRaisedEvent => current,
            DecisionPointResolvedEvent => current,
            SlotBlockedEvent => current,
            OperationRequestIssuedEvent => current,
            OperationRequestAnsweredEvent => current,
            OperationRequestVoidedEvent => current,

            _ => throw new InvalidOperationException($"未知事件类型：{gameEvent.GetType().Name}"),
        };
    }

    private static GameState ApplySeatStateChanged(GameState state, SeatStateChangedEvent changed)
    {
        if (changed.Life is null
            && changed.Character is null
            && changed.Alignment is null
            && changed.Drunk is null
            && changed.Poison is null)
        {
            throw new InvalidOperationException(
                $"事件流损坏：座位 {changed.Seat} 的状态变化事件没有携带任何观测维度");
        }

        var existing = state.Seat(changed.Seat);
        var previousCharacter = existing?.CharacterValue;

        var entry = (existing ?? new SeatStateEntry { Seat = changed.Seat }) with
        {
            Life = ObservedFact(changed.Life, changed, existing?.Life),
            Character = ObservedFact(changed.Character, changed, existing?.Character),
            Alignment = ObservedFact(changed.Alignment, changed, existing?.Alignment),
            Drunk = ObservedFact(changed.Drunk, changed, existing?.Drunk),
            Poison = ObservedFact(changed.Poison, changed, existing?.Poison),
        };

        return TerminateEffectsSourcedBy(ReplaceSeat(state, entry), changed, previousCharacter);
    }

    /// <summary>
    /// 本次观测到的维度写入新事实；未观测的维度保留原有事实（六维度相互独立，不许顺手带改）。
    /// </summary>
    private static StateFact<T>? ObservedFact<T>(
        T? observed,
        SeatStateChangedEvent changed,
        StateFact<T>? previous)
        where T : struct =>
        observed is { } value
            ? new StateFact<T> { Value = value, Reason = changed.Reason, CausedBy = changed.CausedBy }
            : previous;

    /// <summary>
    /// 来源失效传播：来源死亡 → 其持续型效果立即终止；来源换了角色（失去原角色能力）同样终止。
    /// 醉酒 / 中毒**不终止**效果，只是让它暂时不生效（<see cref="PersistentEffect.IsOperative"/>）。
    /// </summary>
    private static GameState TerminateEffectsSourcedBy(
        GameState state,
        SeatStateChangedEvent changed,
        CharacterId? previousCharacter)
    {
        var termination = BuildTermination(changed, previousCharacter);
        if (termination is null)
        {
            return state;
        }

        var effects = state.PersistentEffects
            .Select(effect => effect.Source == changed.Seat && !effect.IsTerminated
                ? effect.Terminate(termination)
                : effect)
            .ToArray();

        return state with { PersistentEffects = effects };
    }

    private static EffectTermination? BuildTermination(SeatStateChangedEvent changed, CharacterId? previousCharacter)
    {
        if (changed.Life == LifeState.Dead)
        {
            return new EffectTermination
            {
                Kind = EffectTerminationKind.SourceDied,
                Reason = $"来源席位 {changed.Seat} 死亡，其持续型效果立即终止（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };
        }

        if (changed.Character is { } newCharacter
            && previousCharacter is { } oldCharacter
            && oldCharacter != newCharacter)
        {
            return new EffectTermination
            {
                Kind = EffectTerminationKind.SourceLostAbility,
                Reason = $"来源席位 {changed.Seat} 的角色由 {oldCharacter} 变为 {newCharacter}，"
                    + $"原角色能力不再存在，其持续型效果立即终止（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };
        }

        return null;
    }

    private static GameState ApplyPersistentEffectApplied(GameState state, PersistentEffectAppliedEvent applied)
    {
        if (state.PersistentEffects.Any(effect => effect.Id == applied.Effect.Id))
        {
            throw new InvalidOperationException(
                $"事件流损坏：持续型效果 {applied.Effect.Id} 已经存在，不能重复施加");
        }

        return state with { PersistentEffects = [.. state.PersistentEffects, applied.Effect] };
    }

    private static GameState ApplyPersistentEffectTerminated(
        GameState state,
        PersistentEffectTerminatedEvent terminated)
    {
        var index = -1;
        for (var i = 0; i < state.PersistentEffects.Count; i++)
        {
            if (state.PersistentEffects[i].Id == terminated.EffectId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"事件流损坏：终止了一条不存在的持续型效果 {terminated.EffectId}");
        }

        if (state.PersistentEffects[index].IsTerminated)
        {
            throw new InvalidOperationException(
                $"事件流损坏：持续型效果 {terminated.EffectId} 已经终止，不能重复终止");
        }

        var effects = state.PersistentEffects.ToArray();
        effects[index] = effects[index].Terminate(terminated.Termination);
        return state with { PersistentEffects = effects };
    }

    private static GameState ApplyMadnessRequirementIssued(GameState state, MadnessRequirementIssuedEvent issued)
    {
        var requirement = issued.Requirement;
        var existing = state.Seat(requirement.Seat) ?? new SeatStateEntry { Seat = requirement.Seat };
        var entry = existing with { Madnesses = [.. existing.Madnesses, requirement] };
        return ReplaceSeat(state, entry);
    }

    /// <summary>写入一行并保持座位按席位号升序——顺序确定是重放可对齐的前提（D-0008）。</summary>
    private static GameState ReplaceSeat(GameState state, SeatStateEntry entry)
    {
        var seats = state.Seats.Where(existing => existing.Seat != entry.Seat).ToList();
        seats.Add(entry);
        seats.Sort((left, right) => left.Seat.Value.CompareTo(right.Seat.Value));
        return state with { Seats = seats };
    }
}
