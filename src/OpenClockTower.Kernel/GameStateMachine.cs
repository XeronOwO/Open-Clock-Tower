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

            // 离场申请与裁定（D-0037）：改的是账里的待批表，不压维度、不产生效果。
            // 申请可以由旅行者在任何时刻提出（含开局前），裁定由说书人给出。
            TravellerDepartureRequestedEvent requested => ApplyDepartureRequested(current, requested),
            TravellerDepartureResolvedEvent resolved => ApplyDepartureResolved(current, resolved),

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
            // 开夜：推进近期活动账的夜晚窗口起点（R-0057-C 的 `last-night` 边界）；六维度与效果不变。
            PhaseStartedEvent started => current with
            {
                Activity = SeatActivityFolder.PhaseStarted(current.Activity, started),
            },
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
            SlotInsertedEvent => current,
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

            // 博学者的白天提问（R-0057）：与艺术家同族——改的是步骤机状态（进行中提问与「今天要过」的账），
            // 两条信息走 InformationResultIssuedEvent 进事件流按收件人投影。
            SavantQuestionAskedEvent => current,
            SavantQuestionClosedEvent => current,

            // 方古的「限一次」标记（R-0034）：整局事实记在步骤机状态里，不改六维度与效果；
            // 侵染产生的角色 / 阵营变化与死亡另有配套的 SeatStateChangedEvent 折进账里。
            FangGuInfectionRecordedEvent => current,

            // 杂耍艺人的公开猜测（R-0057-B）：它是**白天账**里的公开事实（折进 DayRecord），
            // 既不压维度也不产生效果，六维度与效果不变。
            JugglerGuessesMadeEvent => current,

            // 亡骨魔杀死爪牙（R-0056）：记进状态账（保留能力窗口与邻近镇民中毒都由常驻来源按它派生）。
            VigormortisKillRecordedEvent recorded => ApplyVigormortisKillRecorded(current, recorded),

            // 白天流程事件：它们改变的是步骤机状态里的白天账（StepMachineFolder），不改六维度与效果；
            // 处决产生的死亡由配套的 SeatStateChangedEvent 折进账里（处决 ≠ 死亡，百科《处决》）。
            // 例外：黎明要推进失效账本的窗口起点（R-0004 第 2 条，见 ApplyDawn）——六维度与效果仍不变。
            DayStartedEvent started => ApplyDawn(current, started),
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
            ExtraNominationWindowOpenedEvent => current,
            ExtraNominationMadeEvent => current,
            // 处决事实进近期活动账（处决 ≠ 死亡：死亡另由配套的 SeatStateChangedEvent 记一条，R-0057-C）。
            ExecutedEvent executed => current with
            {
                Activity = SeatActivityFolder.Executed(current.Activity, executed),
            },
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

        // 先写入新事实，再记「账上真的变了什么」（R-0057-C），最后做来源失效传播。
        var replaced = ReplaceSeat(state, entry);
        var observed = replaced with
        {
            Activity = SeatActivityFolder.SeatChanged(replaced.Activity, changed, existing),
        };
        return EffectSourceTermination.Apply(observed, changed);
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

        // 有待批的离场申请时必须先结清它（D-0037）：否则复盘里会出现"申请悬着、人已经走了"
        // 这种自相矛盾的账。产出方（TravellerCommandDispatch）一律先写结清事件再写离场事件。
        if (state.DepartureRequestOf(departed.Seat) is { } openRequest)
        {
            throw new InvalidOperationException(
                $"事件流损坏：席位 {departed.Seat.Value} 还有一条待批的离场申请（{openRequest.Note ?? "无说明"}），"
                + "离场之前必须先结清它");
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

        var regained = state.PersistentEffects
            .Where(effect => !effect.IsTerminated
                && (effect.Source == departed.Seat || effect.Target == departed.Seat)
                && effect.Window is not null)
            .ToArray();

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

        var next = state with
        {
            Seats = seats,
            DepartedSeats = [.. state.DepartedSeats, departed.Seat],
            PersistentEffects = effects,
        };

        // 离场把某条能力窗口收掉时（来源 = 集骨者 / 亡骨魔离场，或目标离场），
        // 被重获（R-0054）或保留（R-0056）的能力同步失去。
        foreach (var window in regained)
        {
            next = AbilityWindowDependentTermination.Terminate(next, window, effectTermination);
        }

        return next;
    }

    /// <summary>
    /// 登记一条离场申请（D-0037）：同一席位同时只能有一条待批申请——重复申请属于事件流损坏
    /// （命令侧已经在受理前拒绝，这里是第二道网，顺序损坏不静默丢一条）。
    /// </summary>
    private static GameState ApplyDepartureRequested(
        GameState state,
        TravellerDepartureRequestedEvent requested)
    {
        if (state.DepartureRequestOf(requested.Seat) is { } existing)
        {
            throw new InvalidOperationException(
                $"事件流损坏：席位 {requested.Seat.Value} 已经有一条待批的离场申请（{existing.Note ?? "无说明"}），"
                + "不能重复提出");
        }

        return state with
        {
            DepartureRequests =
            [
                .. state.DepartureRequests,
                new TravellerDepartureRequest { Seat = requested.Seat, Note = requested.Note },
            ],
        };
    }

    /// <summary>结清一条离场申请（批准或驳回）：没有待批申请却要结清属于事件流损坏。</summary>
    private static GameState ApplyDepartureResolved(
        GameState state,
        TravellerDepartureResolvedEvent resolved)
    {
        if (state.DepartureRequestOf(resolved.Seat) is null)
        {
            throw new InvalidOperationException(
                $"事件流损坏：席位 {resolved.Seat.Value} 没有待批的离场申请，却要结清它");
        }

        return state with
        {
            DepartureRequests =
            [
                .. state.DepartureRequests.Where(request => request.Seat != resolved.Seat),
            ],
        };
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
        var terminatedEffect = effects[index].Terminate(terminated.Termination);
        effects[index] = terminatedEffect;

        var next = state with { PersistentEffects = effects };
        return terminatedEffect.Window is not null
            ? AbilityWindowDependentTermination.Terminate(next, terminatedEffect, terminated.Termination)
            : next;
    }

    /// <summary>
    /// 记下「亡骨魔杀死爪牙」（R-0056）：保留能力窗口与邻近镇民中毒都由常驻来源按它派生，
    /// 因此这里只做**流完整性**校验——同一名爪牙不能被杀两次，中毒侧必须是已定义的方向。
    /// </summary>
    /// <remarks>
    /// 不校验"目标此刻已死"：这条事实可能排在死亡事件**之前**（顺序有语义，见
    /// <see cref="PitHagNightMachine.AppendOutcome"/>），折叠不该依赖事件顺序之外的现状。
    /// </remarks>
    private static GameState ApplyVigormortisKillRecorded(
        GameState state,
        VigormortisKillRecordedEvent recorded)
    {
        if (recorded.Side is { } side && !Enum.IsDefined(side))
        {
            throw new InvalidOperationException($"事件流损坏：未知的中毒侧 {side}");
        }

        if (state.VigormortisKills.Any(kill => kill.Minion == recorded.Minion))
        {
            throw new InvalidOperationException(
                $"事件流损坏：席位 {recorded.Minion.Value} 已经记过一次「被亡骨魔杀死」");
        }

        return state with
        {
            VigormortisKills =
            [
                .. state.VigormortisKills,
                new VigormortisKill
                {
                    Demon = recorded.Demon,
                    Minion = recorded.Minion,
                    Side = recorded.Side,
                },
            ],
        };
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
            Activity = SeatActivityFolder.AbilityResolved(state.Activity, resolved),
        };

    /// <summary>黎明：推进失效账本的窗口起点——R-0004 第 2 条「从上一个黎明到数学家被唤醒」的边界。</summary>
    /// <remarks>
    /// 不删记录（R-0004 第 4 条：记录与数学家是否在场无关）；首夜还没有黎明，窗口起点保持 0（全账）。
    /// </remarks>
    private static GameState ApplyDawn(GameState state, DayStartedEvent started) =>
        state with
        {
            Malfunctions = state.Malfunctions.AdvanceDawn(),
            Activity = SeatActivityFolder.Dawn(state.Activity, started.DayNumber),
        };

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
