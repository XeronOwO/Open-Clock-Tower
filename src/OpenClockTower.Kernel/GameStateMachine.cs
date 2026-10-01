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
            AbilityResolvedEvent resolved => ApplyAbilityResolved(current, resolved),

            // 与状态账无关的事件：步骤机推进、请求生命周期、控制模式、裁定点。
            // 信息类结果是发给单个玩家的秘密，不进账（只在事件流里按收件人投影）。
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
            InformationResultIssuedEvent => current,

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

        var entry = (existing ?? new SeatStateEntry { Seat = changed.Seat }) with
        {
            Life = ObservedFact(changed.Life, changed, existing?.Life),
            Character = ObservedFact(changed.Character, changed, existing?.Character),
            Alignment = ObservedFact(changed.Alignment, changed, existing?.Alignment),
            Drunk = ObservedFact(changed.Drunk, changed, existing?.Drunk),
            Poison = ObservedFact(changed.Poison, changed, existing?.Poison),
        };

        return TerminateEffectsLosingAbility(ReplaceSeat(state, entry), changed);
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
            ? new StateFact<T>
            {
                Value = value,
                Reason = changed.Reason,
                CausedBy = changed.CausedBy,
                EffectId = changed.EffectId,
            }
            : previous;

    /// <summary>
    /// 来源失效传播（《重要细节》二-3 / 二-7，口径见 <c>docs/standard/rulings.md</c> R-0012）：
    /// 来源死亡 → 它施加的持续型效果**立即终止**；来源的角色已不是施加该效果时的角色
    /// （= 失去了原角色能力）→ 同样终止。醉酒 / 中毒**不终止**，只是暂时不生效。
    /// </summary>
    /// <remarks>
    /// "角色是不是变了"用**效果自己记录的 <see cref="PersistentEffect.SourceCharacter"/>** 判定，
    /// 而不是"上一次观测到的角色"：后者在来源角色从未被观测过时会静默漏判，
    /// 让一条早就该终止的效果继续被算成生效。
    /// </remarks>
    private static GameState TerminateEffectsLosingAbility(GameState state, SeatStateChangedEvent changed)
    {
        var termination = BuildTermination(changed);
        if (termination is null)
        {
            return state;
        }

        var effects = state.PersistentEffects
            .Select(effect => LosesAbility(effect, changed) ? effect.Terminate(termination) : effect)
            .ToArray();

        return state with { PersistentEffects = effects };
    }

    /// <summary>来源死亡一律终止；来源角色与效果记录的施加时角色不同也终止。已终止的不重复处理。</summary>
    private static bool LosesAbility(PersistentEffect effect, SeatStateChangedEvent changed) =>
        effect.Source == changed.Seat
        && !effect.IsTerminated
        && (changed.Life == LifeState.Dead
            || (changed.Character is { } character && character != effect.SourceCharacter));

    private static EffectTermination? BuildTermination(SeatStateChangedEvent changed)
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

        if (changed.Character is { } character)
        {
            return new EffectTermination
            {
                Kind = EffectTerminationKind.SourceLostAbility,
                Reason = $"来源席位 {changed.Seat} 的角色已变为 {character}，不再是施加该效果时的角色，"
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

        // 与效果路径同一失败姿态：同一个裁定点重复签发同一条要求属于事件流损坏，不许静默堆两条。
        if (existing.Madnesses.Any(current =>
                current.IssuedBy == requirement.IssuedBy
                && current.Seat == requirement.Seat
                && string.Equals(current.ProveToBe, requirement.ProveToBe, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"事件流损坏：裁定点 {requirement.IssuedBy} 对座位 {requirement.Seat} 的疯狂要求已经存在，不能重复签发");
        }

        var entry = existing with { Madnesses = [.. existing.Madnesses, requirement] };
        return ReplaceSeat(state, entry);
    }

    /// <summary>
    /// 能力结算 → 两本账：一次使用无论是否生效都记「用过」（三-3：醉酒 / 中毒期间使用即被浪费）；
    /// 只有未正常生效才进失效账本，分类原样保留（R-0004；Open 的留在待核对清单里）。
    /// </summary>
    private static GameState ApplyAbilityResolved(GameState state, AbilityResolvedEvent resolved) =>
        state with
        {
            AbilityUses = state.AbilityUses.RecordUse(
                resolved.Actor,
                resolved.Ability,
                resolved.Effective),
            Malfunctions = resolved.Malfunction is { } kind
                ? state.Malfunctions.Record(resolved.Actor, resolved.Ability, kind)
                : state.Malfunctions,
        };

    /// <summary>写入一行并保持座位按席位号升序——顺序确定是重放可对齐的前提（D-0008）。</summary>
    private static GameState ReplaceSeat(GameState state, SeatStateEntry entry)
    {
        var seats = state.Seats.Where(existing => existing.Seat != entry.Seat).ToList();
        seats.Add(entry);
        seats.Sort((left, right) => left.Seat.Value.CompareTo(right.Seat.Value));
        return state with { Seats = seats };
    }
}
