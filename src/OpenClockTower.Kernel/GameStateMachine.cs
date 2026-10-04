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

            // 旅行者加入：六维度账由同批的 SeatStateChangedEvent 落地（角色 / 阵营 / 初始生死），
            // 本事件只保留"以旅行者身份入场"这一事实——公开宣告、复盘与投影读它（D-0010）。
            TravellerJoinedEvent => current,

            // 旅行者离场：席位账移除 + 离场账登记 + 相关持续型效果 / 疯狂要求终止（R-0044 第 6 条）。
            TravellerDepartedEvent departed => ApplyTravellerDeparted(current, departed),

            PersistentEffectAppliedEvent applied => ApplyPersistentEffectApplied(current, applied),
            PersistentEffectTerminatedEvent terminated => ApplyPersistentEffectTerminated(current, terminated),
            InstantaneousEffectAppliedEvent applied => current with
            {
                InstantaneousEffects = [.. current.InstantaneousEffects, applied.Effect],
            },
            MadnessRequirementIssuedEvent issued => ApplyMadnessRequirementIssued(current, issued),
            MadnessRequirementTerminatedEvent terminated => ApplyMadnessRequirementTerminated(current, terminated),
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
            SlotUnblockedEvent => current,
            OperationRequestIssuedEvent => current,
            OperationRequestAnsweredEvent => current,
            OperationRequestVoidedEvent => current,
            InformationResultIssuedEvent => current,

            // 说书人注记（D-0019）：自由文本**不进状态账**（D-0015）——它折进独立的注记账
            // （SeatAnnotationMachine），只说书人可见；改 / 删同样不改六维度与效果。
            SeatAnnotationAddedEvent => current,
            SeatAnnotationUpdatedEvent => current,
            SeatAnnotationRemovedEvent => current,

            // 胜负结论与呆瓜选择：它们改变的是步骤机状态里的结束态 / 选择账（StepMachineFolder），
            // 不改六维度与效果；胜负求值直接读本批事件 + 当前账（R-0024 / R-0027）。
            GameEndedEvent => current,
            KlutzChoiceMadeEvent => current,
            KlutzChoiceSkippedEvent => current,

            // 槽位激活与麻脸巫婆之夜的死亡裁量：它们改的都是**步骤机状态**
            // （计划里的那一格、窗口与待定死亡表），不改六维度与效果。
            // 待定死亡落成死亡事实时另有配套的 SeatStateChangedEvent 折进账里。
            SlotActivatedEvent => current,
            PitHagNightOpenedEvent => current,
            DeferredDeathRecordedEvent => current,
            DeferredDeathResolvedEvent => current,
            PitHagNightClosedEvent => current,

            // 「今晚理发」事实（R-0033）：改的同样是步骤机状态，不改六维度与效果；
            // 交换产生的两条角色变化另有配套的 SeatStateChangedEvent 折进账里。
            BarberNightOpenedEvent => current,
            BarberNightClosedEvent => current,
            BarberNightSkippedEvent => current,

            // 贤者事实（R-0038）与心上人跳过账（R-0039）：改的同样是步骤机状态，不改六维度与效果；
            // 心上人的醉酒效果走 PersistentEffectAppliedEvent 进效果账，维度变化由结算对账产出。
            SageNightOpenedEvent => current,
            SageNightClosedEvent => current,
            SageNightSkippedEvent => current,
            SweetheartDeathSkippedEvent => current,

            // 艺术家的白天提问（R-0040）：改的是步骤机状态（进行中问题与裁定点），
            // 问题与回答都不是六维度 / 效果；回答产生的信息走 InformationResultIssuedEvent。
            ArtistQuestionAskedEvent => current,
            ArtistQuestionClosedEvent => current,

            // 方古的「限一次」标记（R-0034）：整局事实记在步骤机状态里，不改六维度与效果；
            // 侵染产生的角色 / 阵营变化与死亡另有配套的 SeatStateChangedEvent 折进账里。
            FangGuInfectionRecordedEvent => current,

            // 白天流程事件：它们改变的是步骤机状态里的白天账（StepMachineFolder），不改六维度与效果；
            // 处决产生的死亡由配套的 SeatStateChangedEvent 折进账里（处决 ≠ 死亡，百科《处决》）。
            // 例外：黎明要推进失效账本的窗口起点（R-0004 第 2 条，见 ApplyDawn）——六维度与效果仍不变。
            DayStartedEvent => ApplyDawn(current),
            NominationMadeEvent => current,
            VoteCastEvent => current,
            VoteSweepStartedEvent => current,
            SeatVoteCollectedEvent => current,
            VoteSweepResumedEvent => current,
            VoteCountedEvent => current,
            ExileProposedEvent => current,
            ExileVoteCastEvent => current,
            ExileSweepStartedEvent => current,
            ExileSeatVoteCollectedEvent => current,
            ExileSweepResumedEvent => current,
            ExileVoteCountedEvent => current,
            DayProtectionDecidedEvent => current,
            ExecutedEvent => current,
            DayClosedEvent => current,

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
    /// 来源失效传播（《重要细节》二-3 / 二-7，口径见 <c>docs/standard/rulings.md</c> R-0012 与 R-0021）：
    /// 来源死亡 → 它施加的持续型效果与下达的疯狂要求**立即终止**；来源的角色已不是施加时的角色
    /// （= 失去了原角色能力）→ 同样终止。醉酒 / 中毒**不终止**，只是暂时不生效。
    /// </summary>
    /// <remarks>
    /// "角色是不是变了"用**效果 / 要求自己记录的施加时角色**判定，而不是"上一次观测到的角色"：
    /// 后者在来源角色从未被观测过时会静默漏判，让一条早就该终止的效果继续被算成生效。
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

        // 疯狂要求与持续型效果同源同命运：来源死亡 / 换角色 → 立即撤下（R-0021）。
        // 目标**自己**的死亡 / 换角不撤下要求——已死亡的目标仍可能因不够疯狂被处决（R-0021）。
        var requirementTermination = BuildRequirementTermination(changed);
        var seats = state.Seats
            .Select(entry => entry.Madnesses.Any(requirement => LosesRequirementAbility(requirement, changed))
                ? entry with
                {
                    Madnesses = [.. entry.Madnesses.Select(requirement =>
                        LosesRequirementAbility(requirement, changed)
                            ? requirement.Terminate(requirementTermination)
                            : requirement)],
                }
                : entry)
            .ToArray();

        return state with { PersistentEffects = effects, Seats = seats };
    }

    /// <summary>
    /// 旅行者离场（百科《旅行者》· 2026-10-04 抓取 · 离开流程；`rulings.md` R-0044 第 6 条）：
    /// 席位账移除（角色与生命标记一并移除）、离场账登记；以该席位为**来源或目标**的持续型效果、
    /// 以及它下达的疯狂要求立即终止——离场后它们既没有来源、也没有对象。
    /// 席位票据与座位号保留在会话信息里（不是本账的事）。
    /// </summary>
    private static GameState ApplyTravellerDeparted(GameState state, TravellerDepartedEvent departed)
    {
        if (state.HasDeparted(departed.Seat))
        {
            throw new InvalidOperationException(
                $"事件流损坏：席位 {departed.Seat.Value} 已经离场，不能重复离场");
        }

        var note = string.IsNullOrWhiteSpace(departed.Note) ? string.Empty : $"；说书人说明：{departed.Note}";
        var effectTermination = new EffectTermination
        {
            Kind = EffectTerminationKind.SeatLeftGame,
            Reason = $"席位 {departed.Seat.Value} 以旅行者身份离场：角色与生命标记一并移除，"
                + $"以它为来源 / 目标的持续型效果立即终止{note}"
                + "（百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式；rulings.md R-0044 第 6 条）",
            CausedBy = departed.Seat,
        };

        var effects = state.PersistentEffects
            .Select(effect => !effect.IsTerminated && (effect.Source == departed.Seat || effect.Target == departed.Seat)
                ? effect.Terminate(effectTermination)
                : effect)
            .ToArray();

        // 疯狂要求与持续型效果同源同命运（R-0021）：要求记在**目标席位的账**上，
        // 所以"来源离场"要逐个目标席位扫一遍，不能只删自己那一条。
        var requirementTermination = effectTermination with
        {
            Reason = $"席位 {departed.Seat.Value} 离场：它下达的疯狂要求立即撤下{note}"
                + "（rulings.md R-0021 / R-0044 第 6 条）",
        };
        var seats = state.Seats
            .Where(entry => entry.Seat != departed.Seat)
            .Select(entry => entry.Madnesses.Any(requirement =>
                    !requirement.IsTerminated && requirement.Source == departed.Seat)
                ? entry with
                {
                    Madnesses =
                    [
                        .. entry.Madnesses.Select(requirement =>
                            !requirement.IsTerminated && requirement.Source == departed.Seat
                                ? requirement.Terminate(requirementTermination)
                                : requirement),
                    ],
                }
                : entry)
            .ToArray();

        return state with
        {
            Seats = seats,
            DepartedSeats = [.. state.DepartedSeats, departed.Seat],
            PersistentEffects = effects,
        };
    }

    /// <summary>来源死亡一律终止；来源角色与效果记录的施加时角色不同也终止。已终止的不重复处理。</summary>
    private static bool LosesAbility(PersistentEffect effect, SeatStateChangedEvent changed) =>
        effect.Source == changed.Seat
        && !effect.IsTerminated
        && (changed.Life == LifeState.Dead
            || (changed.Character is { } character && character != effect.SourceCharacter));

    /// <summary>疯狂要求的同款判定：来源死亡或换角色即撤下。</summary>
    private static bool LosesRequirementAbility(MadnessRequirement requirement, SeatStateChangedEvent changed) =>
        requirement.Source == changed.Seat
        && !requirement.IsTerminated
        && (changed.Life == LifeState.Dead
            || (changed.Character is { } character && character != requirement.SourceCharacter));

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

    /// <summary>疯狂要求的撤下说明：与效果终止同一分类，措辞换成"要求"。</summary>
    private static EffectTermination BuildRequirementTermination(SeatStateChangedEvent changed) =>
        changed.Life == LifeState.Dead
            ? new EffectTermination
            {
                Kind = EffectTerminationKind.SourceDied,
                Reason = $"来源席位 {changed.Seat} 死亡，它下达的疯狂要求立即撤下（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            }
            : new EffectTermination
            {
                Kind = EffectTerminationKind.SourceLostAbility,
                Reason = $"来源席位 {changed.Seat} 的角色已发生变化，原角色能力不再存在，"
                    + $"它下达的疯狂要求立即撤下（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };

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

        // 与效果路径同一失败姿态：同一个标识重复签发属于事件流损坏，不许静默堆两条。
        if (existing.Madnesses.Any(current => current.Id == requirement.Id))
        {
            throw new InvalidOperationException(
                $"事件流损坏：疯狂要求 {requirement.Id} 已经存在，不能重复签发");
        }

        var entry = existing with { Madnesses = [.. existing.Madnesses, requirement] };
        return ReplaceSeat(state, entry);
    }

    /// <summary>
    /// 撤下一条疯狂要求（到期 / 来源死亡 / 来源换角色 / 说书人作废，R-0021）。
    /// 与效果终止同一姿态：找不到或重复终止一律显式失败，恢复不得静默继续（D-0014 能力 3）。
    /// </summary>
    private static GameState ApplyMadnessRequirementTerminated(
        GameState state,
        MadnessRequirementTerminatedEvent terminated)
    {
        foreach (var entry in state.Seats)
        {
            var index = -1;
            for (var i = 0; i < entry.Madnesses.Count; i++)
            {
                if (entry.Madnesses[i].Id == terminated.Id)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                continue;
            }

            if (entry.Madnesses[index].IsTerminated)
            {
                throw new InvalidOperationException(
                    $"事件流损坏：疯狂要求 {terminated.Id} 已经撤下，不能重复撤下");
            }

            var requirements = entry.Madnesses.ToArray();
            requirements[index] = requirements[index].Terminate(terminated.Termination);
            return ReplaceSeat(state, entry with { Madnesses = requirements });
        }

        throw new InvalidOperationException($"事件流损坏：撤下了一条不存在的疯狂要求 {terminated.Id}");
    }

    /// <summary>
    /// 能力结算 → 两本账：一次使用无论是否生效都记「用过」（三-3：醉酒 / 中毒期间使用即被浪费）；
    /// 未正常生效 / 受干扰的分类**逐条**进失效账本（R-0004：一次结算可并列多条，不硬塞、不抵消）。
    /// </summary>
    private static GameState ApplyAbilityResolved(GameState state, AbilityResolvedEvent resolved) =>
        state with
        {
            AbilityUses = state.AbilityUses.RecordUse(
                resolved.Actor,
                resolved.Ability,
                resolved.Effective),
            Malfunctions = RecordMalfunctions(state.Malfunctions, resolved),
        };

    /// <summary>黎明：推进失效账本的窗口起点——R-0004 第 2 条「从上一个黎明到数学家被唤醒」的边界。</summary>
    /// <remarks>
    /// 不删记录（R-0004 第 4 条：记录与数学家是否在场无关）；首夜还没有黎明，窗口起点保持 0（全账）。
    /// </remarks>
    private static GameState ApplyDawn(GameState state) =>
        state with { Malfunctions = state.Malfunctions.AdvanceDawn() };

    /// <summary>按事件里记录的分类逐条追加，顺序与事件一致（R-0004）。</summary>
    private static MalfunctionLedger RecordMalfunctions(
        MalfunctionLedger ledger,
        AbilityResolvedEvent resolved)
    {
        foreach (var kind in resolved.Malfunctions)
        {
            ledger = ledger.Record(resolved.Actor, resolved.Ability, kind);
        }

        return ledger;
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
