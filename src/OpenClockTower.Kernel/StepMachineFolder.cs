namespace OpenClockTower.Kernel;

/// <summary>
/// 把事件流折叠回步骤机状态——重放、重启恢复、撤销的共同基础。
/// </summary>
/// <remarks>
/// 从 <see cref="StepMachine"/> 拆出：`Handle` 负责"产事件"，这里负责"把事件折回状态"。
/// 顺序损坏一律显式抛错（D-0014 能力 3）。账事件（座位状态 / 效果 / 疯狂要求）不创建状态：
/// 它们可以先于任何阶段出现（开局分配），此时步骤机保持"尚未开始"（null）。
/// </remarks>
internal static class StepMachineFolder
{
    /// <summary>把一条事件折叠回状态。</summary>
    internal static StepMachineState? Apply(StepMachineState? state, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            PhaseStartedEvent started => new StepMachineState
            {
                Plan = started.Plan,
                SlotIndex = 0,
                Quota = SlotQuotaState.Running,
                Control = started.Control,

                // 白天账跨阶段保留：死亡玩家的「死后仅一次投票」与逐日事实（卖花女孩 / 城镇公告员要读）
                // 不随夜晚开始清零。
                Day = state?.Day,

                // 胜负结论与呆瓜选择账同样跨阶段保留：结束后不允许再开新阶段（R-0024），
                // 呆瓜选择的幂等依据也不能随阶段遗忘（R-0027）。
                Outcome = state?.Outcome,
                KlutzChoices = state?.KlutzChoices ?? [],

                // 方古的「限一次」标记整局保留：即使原方古死亡 / 换角、之后又出现新的方古，
                // 也不再侵染（百科《方古》· 2026-10-01 抓取 · 运作方式 14；R-0034）。
                FangGuInfection = state?.FangGuInfection,

                // 「今晚理发」事实跨阶段保留：白天死亡 → 当夜交互（R-0033）。
                // 从**夜晚**带进新阶段说明夜末的「过时不候」收口缺失——CarryBarberNight 显式失败。
                BarberNight = DeathTriggerFolder.CarryBarberNight(state),

                // 贤者事实只属于当夜：阶段边界上仍挂着说明夜末收口缺失——CarrySageNight 显式失败。
                SageNight = DeathTriggerFolder.CarrySageNight(state),

                // 心上人跳过的账同样跨阶段保留：幂等依据不能随阶段遗忘（R-0039）。
                SweetheartSkips = state?.SweetheartSkips ?? [],

                // 艺术家的进行中提问不能跨阶段：边界上仍挂着说明收口缺失——CarryAcrossPhase 显式失败。
                ArtistQuestion = ArtistQuestionFolder.CarryAcrossPhase(state),

                // 博学者的进行中提问同样不能跨阶段；「今天已经要过」的账**不携带**——
                // 新白天自然可以再要一次（R-0057）。
                SavantQuestion = SavantQuestionFolder.CarryAcrossPhase(state),
                SavantAskedSeat = null,
            },
            SlotEnteredEvent entered => ApplySlotEntered(Require(state, entered), entered),
            OperationRequestIssuedEvent issued => Require(state, issued) with
            {
                PendingRequest = issued.Request,
            },
            OperationRequestAnsweredEvent answered => Require(state, answered) with
            {
                PendingRequest = RequireOpenPending(state, answered.RequestId) with
                {
                    Status = OperationRequestStatus.Answered,
                    Answer = answered.Answer,
                },
            },
            OperationRequestVoidedEvent voided => Require(state, voided) with
            {
                PendingRequest = RequireOpenPending(state, voided.RequestId) with
                {
                    Status = OperationRequestStatus.Voided,
                    Voided = voided.Void,
                },
            },
            SlotQuotaElapsedEvent elapsed => Require(state, elapsed) with
            {
                Quota = SlotQuotaState.Elapsed,
            },
            SlotAdvancedEvent advanced => ApplyAdvance(state, advanced.FromIndex, advanced.ToIndex),
            SlotForceAdvancedEvent forceAdvanced => ApplyAdvance(state, forceAdvanced.FromIndex, forceAdvanced.ToIndex),
            PhaseCompletedEvent completed => Require(state, completed),
            ControlModeChangedEvent control => Require(state, control) with { Control = control.Mode },
            PromptSkippedEvent skipped => Require(state, skipped),
            SlotActivatedEvent activated => SlotActivationFolder.Apply(state, activated),

            // 槽位追加（R-0055）：非首个夜晚获得的「首个夜晚」能力在顺序表上没有位置，
            // 由结算契约追加一格（与激活的区别：那一格本来不存在）。
            SlotInsertedEvent inserted => SlotInsertionFolder.Apply(state, inserted),

            // 麻脸巫婆之夜的死亡裁量窗口（R-0030）。
            PitHagNightOpenedEvent opened => ApplyPitHagNightOpened(state, opened),
            DeferredDeathRecordedEvent recorded => ApplyDeferredDeathRecorded(state, recorded),
            DeferredDeathResolvedEvent deferredResolved => ApplyDeferredDeathResolved(state, deferredResolved),
            PitHagNightClosedEvent closed => ApplyPitHagNightClosed(state, closed),

            // 「今晚理发」事实（R-0033）：开启 / 关闭改步骤机状态；跳过事件只在事件流里留痕。
            BarberNightOpenedEvent barberOpened => DeathTriggerFolder.ApplyBarberNightOpened(state, barberOpened),
            BarberNightClosedEvent barberClosed => DeathTriggerFolder.ApplyBarberNightClosed(state, barberClosed),
            BarberNightSkippedEvent => state,

            // 贤者「被恶魔杀死」事实（R-0038）：开启 / 关闭改步骤机状态；跳过事件只在事件流里留痕。
            SageNightOpenedEvent sageOpened => DeathTriggerFolder.ApplySageNightOpened(state, sageOpened),
            SageNightClosedEvent sageClosed => DeathTriggerFolder.ApplySageNightClosed(state, sageClosed),
            SageNightSkippedEvent => state,

            // 心上人的跳过账（R-0039）：幂等依据；醉酒效果本身走 PersistentEffectAppliedEvent，
            // 维度变化交给结算对账（D-0015 推论 1）。
            SweetheartDeathSkippedEvent sweetheartSkipped =>
                DeathTriggerFolder.ApplySweetheartSkip(state, sweetheartSkipped),

            // 艺术家的白天提问（R-0040）：问题进事件流、结清时清空；跨阶段残留由 ApplyPhaseStarted 显式失败。
            ArtistQuestionAskedEvent artistAsked => ArtistQuestionFolder.ApplyAsked(state, artistAsked),
            ArtistQuestionClosedEvent artistClosed => ArtistQuestionFolder.ApplyClosed(state, artistClosed),

            // 博学者的白天提问（R-0057）：与艺术家同族，多一份「今天已经要过」的账（阶段边界清零）。
            SavantQuestionAskedEvent savantAsked => SavantQuestionFolder.ApplyAsked(state, savantAsked),
            SavantQuestionClosedEvent savantClosed => SavantQuestionFolder.ApplyClosed(state, savantClosed),

            // 方古的「限一次」标记（R-0034）：整局事实，落下后不再重复。
            FangGuInfectionRecordedEvent infection => ApplyFangGuInfection(state, infection),

            // 亡骨魔杀死爪牙（R-0056）：事实落在**状态账**（GameStateMachine），步骤机状态不因它改变
            // ——保留能力窗口与邻近镇民中毒由常驻来源按它派生。
            VigormortisKillRecordedEvent => state,

            SeatStateChangedEvent => state,

            // 旅行者加入 / 离场：六维度账与离场账都在 GameStateMachine 折叠；步骤机状态不因席位变化而启动——
            // 两者都可以先于任何阶段出现（首个阶段之前也能加入 / 离场），不允许把 null 变成"已开始"。
            TravellerJoinedEvent => state,
            TravellerDepartedEvent => state,

            // 离场申请与裁定（D-0037）：它们折进**状态账**（GameState.DepartureRequests），
            // 与席位变化同族——都在步骤机之外，都不允许把 null 变成"已开始"。
            TravellerDepartureRequestedEvent => state,
            TravellerDepartureResolvedEvent => state,

            DecisionPointRaisedEvent raised => ApplyDecisionPointRaised(state, raised),
            DecisionPointResolvedEvent resolved => ResolveDecision(state, resolved),
            SlotBlockedEvent blocked => Require(state, blocked) with
            {
                Block = new StepBlock { Reason = blocked.Reason },
            },
            SlotUnblockedEvent unblocked => ApplyUnblock(state, unblocked),

            // 白天事件：折叠白天账（提名 / 投票 / 计票 / 处决 / 结束）；槽位推进由 CloseDay / 强推产出的事件驱动。
            DayStartedEvent => ApplyDay(state, gameEvent),
            NominationMadeEvent => ApplyDay(state, gameEvent),
            VoteCastEvent => ApplyDay(state, gameEvent),
            VoteSweepStartedEvent => ApplyDay(state, gameEvent),
            SeatVoteCollectedEvent => ApplyDay(state, gameEvent),
            VoteSweepResumedEvent => ApplyDay(state, gameEvent),
            VoteCountedEvent => ApplyDay(state, gameEvent),

            // 流放事件（D2）：与提名同族，都折叠进白天账（各自一册，见 ExileLedgerFolder）。
            ExileProposedEvent => ApplyDay(state, gameEvent),
            ExileVoteCastEvent => ApplyDay(state, gameEvent),
            ExileSweepStartedEvent => ApplyDay(state, gameEvent),
            ExileSeatVoteCollectedEvent => ApplyDay(state, gameEvent),
            ExileSweepResumedEvent => ApplyDay(state, gameEvent),
            ExileVoteCountedEvent => ApplyDay(state, gameEvent),

            // 死亡保护裁定（D3）：当天作用域的裁定，同样折进白天账（R-0048）。
            DayProtectionDecidedEvent => ApplyDay(state, gameEvent),

            // 屠夫窗口（D4 / R-0050）：首次处决后的开窗与窗口内的额外提名，都折进当天账。
            ExtraNominationWindowOpenedEvent or ExtraNominationMadeEvent => ApplyDay(state, gameEvent),

            // 杂耍艺人的公开猜测（R-0057-B）：当天公开事实，折进白天账（与上面几族同一路径）。
            JugglerGuessesMadeEvent => ApplyDay(state, gameEvent),

            // 夜晚处罚处决（DayNumber = null）不写白天账：不把"还没有白天"物化成空账（R-0020）。
            ExecutedEvent { DayNumber: null } => Require(state, gameEvent),
            ExecutedEvent => ApplyDay(state, gameEvent),
            DayClosedEvent => ApplyDay(state, gameEvent),

            // 胜负结论与呆瓜选择账：两条都是步骤机自己的账（R-0024 / R-0027）。
            GameEndedEvent ended => ApplyGameEnded(state, ended),
            KlutzChoiceMadeEvent choice => DeathTriggerFolder.ApplyKlutzChoice(state, choice),
            KlutzChoiceSkippedEvent skipped => DeathTriggerFolder.ApplyKlutzChoiceSkipped(state, skipped),

            // 状态账的事件：进同一条事件流，但步骤机状态不由它们改变
            // （座位状态变化对步骤机的影响是"作废依赖失效的挂起请求"，在 Handle 阶段已经处理完）。
            // 它们可以**先于任何阶段**出现（开局分配与初始状态）——此时步骤机保持"尚未开始"（null）：
            // 账是同一事件流上的独立派生视图（D-0015），不允许账事件把 null 变成"已开始"。
            PersistentEffectAppliedEvent => state,
            PersistentEffectTerminatedEvent => state,
            InstantaneousEffectAppliedEvent => state,
            MadnessRequirementIssuedEvent => state,
            MadnessRequirementTerminatedEvent => state,

            // 能力结算：把「本格已经结算过」记在步骤机上——「行动两次」的第二次结算以此为凭据
            // （跳过 / 作废 / 阻塞的格子没有它）。步骤机还没开始时保持 null：账事件可以先于任何阶段出现。
            AbilityResolvedEvent resolved => state is null ? null : ApplyAbilityResolved(state, resolved),
            InformationResultIssuedEvent => state,

            // 说书人注记（D-0019）：第三条派生视图（SeatAnnotationMachine）的事件，
            // 步骤机状态不由它们改变；它们同样可以先于任何阶段出现（步骤机保持 null）。
            SeatAnnotationAddedEvent => state,
            SeatAnnotationUpdatedEvent => state,
            SeatAnnotationRemovedEvent => state,

            _ => throw new InvalidOperationException($"未知事件类型：{gameEvent.GetType().Name}"),
        };
    }

    /// <summary>
    /// 进入槽位（含同格重进）：清挂起、配额重新起算，并把进入遍次加一
    /// （推进到下一格时由 <see cref="ApplyAdvance"/> 清零，下一格首次进入重新记 1；R-0052）。
    /// </summary>
    private static StepMachineState ApplySlotEntered(StepMachineState state, SlotEnteredEvent entered) =>
        state with
        {
            SlotIndex = entered.SlotIndex,
            Quota = SlotQuotaState.Running,
            PendingRequest = null,
            AwaitingDecision = null,
            AwaitingDecisionTriggerAbility = null,
            AwaitingDecisionSeat = null,
            Block = null,
            SlotPass = state.SlotPass + 1,
            SlotAbilityResolved = false,
        };

    /// <summary>
    /// 一条能力结算落到**当前槽位**上时，把「本格已结算」记下（「行动两次」的判据之一，R-0052）。
    /// 不属于当前槽位的结算（历史槽位 / 触发型请求）不改本格状态。
    /// </summary>
    private static StepMachineState ApplyAbilityResolved(StepMachineState state, AbilityResolvedEvent resolved) =>
        state.CurrentSlot?.Id == resolved.SlotId
            ? state with { SlotAbilityResolved = true }
            : state;

    /// <summary>按序折叠一批事件；空批（或只含账事件）时结果为 null。</summary>
    internal static StepMachineState? ApplyAll(StepMachineState? state, IEnumerable<GameEvent> events)
    {
        var current = state;
        foreach (var gameEvent in events)
        {
            current = Apply(current, gameEvent);
        }

        return current;
    }

    /// <summary>折叠一条事件前必须已有状态；否则事件流顺序损坏（供 <see cref="DeathTriggerFolder"/> 共用）。</summary>
    internal static StepMachineState Require(StepMachineState? state, GameEvent gameEvent) =>
        state ?? throw new InvalidOperationException($"事件流顺序损坏：{gameEvent.GetType().Name} 之前没有状态");

    private static OperationRequest RequireOpenPending(StepMachineState? state, OperationRequestId requestId)
    {
        var pending = state?.PendingRequest;
        if (pending is null || pending.Id != requestId)
        {
            throw new InvalidOperationException($"事件流顺序损坏：{requestId} 不是当前挂起的请求");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            throw new InvalidOperationException($"事件流顺序损坏：请求 {requestId} 已经了结，不能再次了结");
        }

        return pending;
    }

    private static StepMachineState ResolveDecision(StepMachineState? state, DecisionPointResolvedEvent resolved)
    {
        var current = Require(state, resolved);
        var decision = current.AwaitingDecision;
        if (decision is null || decision.Id != resolved.DecisionPointId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{resolved.DecisionPointId} 不是当前挂起的裁定点");
        }

        return current with
        {
            AwaitingDecision = null,
            AwaitingDecisionTriggerAbility = null,
            AwaitingDecisionSeat = null,
        };
    }

    /// <summary>
    /// 解除阻塞报警：**只**清 <see cref="StepMachineState.Block"/>——槽位下标、最小配额、挂起请求与
    /// 裁定点一律不动。这正是"只清阻塞"的内核原语：与推进类事件的区别就在这里，结束批次不得
    /// 伪造推进去顺手清它（那会让投影与快照分叉；票据 terminal-hold-residue、依据 R-0024 / D-0010）。
    /// </summary>
    /// <remarks>
    /// 没有阻塞却要解除、或事件里的槽位与当前槽位对不上，都是事件流损坏：显式失败，不静默继续
    /// （D-0014 能力 3）。阻塞只在**进入槽位**时置位，此后下标不会变——任何进出槽位的事件都会清掉它，
    /// 槽位激活只允许未来槽位。所以 <see cref="StepMachineState.CurrentSlot"/> 就是被阻塞的那一格。
    /// </remarks>
    private static StepMachineState ApplyUnblock(StepMachineState? state, SlotUnblockedEvent unblocked)
    {
        var current = Require(state, unblocked);
        if (current.Block is null)
        {
            throw new InvalidOperationException("事件流顺序损坏：没有阻塞报警，却要解除");
        }

        if (current.CurrentSlot?.Id != unblocked.SlotId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：阻塞报警属于槽位 {current.CurrentSlot?.Id.Value ?? "（无）"}，"
                + $"不是 {unblocked.SlotId.Value}");
        }

        return current with { Block = null };
    }

    /// <summary>
    /// 开一个裁定点：挂起待裁定；事件携带槽位提示时，把它**回写进槽位**（替换计划快照）。
    /// </summary>
    /// <remarks>
    /// 槽位提示是这一步的操作上下文（视图「当前步骤」与后续消费者都读它）。入槽实时重建
    /// （数学家的失效窗口这类上下文）不回写，屏幕就会出现"裁定点是新值、摘要还是计划旧值"的分叉。
    /// </remarks>
    /// <exception cref="InvalidOperationException">事件流顺序损坏：裁定点不属于当前槽位。</exception>
    private static StepMachineState ApplyDecisionPointRaised(StepMachineState? state, DecisionPointRaisedEvent raised)
    {
        var before = Require(state, raised);

        // 来源必须**恰好一个**：槽位（SlotId 非空）或触发（TriggerAbility 非空）。都缺会让
        // 「谁在等说书人」无法归因、推进闸无法区分触发型挂起；都填属于产出方自相矛盾（R-0039）。
        if ((raised.SlotId is null) == (raised.TriggerAbility is null))
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：裁定点 {raised.DecisionPoint.Id} 的来源必须恰好一个"
                + "（槽位来源填 SlotId；触发来源填 TriggerAbility 且不填 SlotId）");
        }

        // 同时只挂一个裁定点：覆盖旧挂起会让它永远答不了（触发器层应在开新裁定前显式跳过，
        // 这里是第二道安全网——顺序损坏必须显式失败，不静默丢一条挂起）。
        if (before.AwaitingDecision is { } pending)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：已有挂起的裁定点 {pending.Id}，不能直接覆盖为 {raised.DecisionPoint.Id}");
        }

        // 归属席位（"谁在等"）：开点来源显式给出（触发格 / 触发型裁定没有行动者，只能靠它）。
        // 该字段是**后加的**可空字段：旧版本事件流里没有它，这里容忍为 null——重放不能因为
        // 一条旧事件就失败；界面在 null 时退回行动者 / 摘要回退，不猜归属（口径见 E25 票据）。
        // 5 处开点的"必填"由 Rules/Kernel/宿主用例锁住，而不是靠重放时抛错。
        var current = before with
        {
            AwaitingDecision = raised.DecisionPoint,
            AwaitingDecisionTriggerAbility = raised.TriggerAbility,
            AwaitingDecisionSeat = raised.AttributionSeat,
        };
        if (raised.SlotPrompt is not { } prompt)
        {
            return current;
        }

        var slot = current.CurrentSlot;
        if (slot is null || raised.SlotId is not { } slotId || slot.Id != slotId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：裁定点 {raised.SlotId?.Value ?? "(触发来源)"} 不在当前槽位上，不能回写槽位提示");
        }

        var slots = current.Plan.Slots.ToArray();
        slots[current.SlotIndex] = slot with { Prompt = prompt };
        return current with { Plan = current.Plan with { Slots = slots } };
    }

    /// <summary>开启麻脸巫婆之夜的死亡裁量窗口；同一夜不能开两次。</summary>
    private static StepMachineState ApplyPitHagNightOpened(StepMachineState? state, PitHagNightOpenedEvent opened)
    {
        var current = Require(state, opened);
        if (current.PitHagNight is not null)
        {
            throw new InvalidOperationException("事件流顺序损坏：麻脸巫婆之夜的死亡裁量窗口已经开启过");
        }

        return current with
        {
            PitHagNight = new PitHagNight
            {
                Source = opened.Source,
                ClosesAfterSlotIndex = opened.ClosesAfterSlotIndex,
                CasualtyAbility = opened.CasualtyAbility,
                Deferred = [],
            },
        };
    }

    /// <summary>记一条待定死亡；没有窗口、或同一席位已有待定死亡一律抛错。</summary>
    private static StepMachineState ApplyDeferredDeathRecorded(
        StepMachineState? state,
        DeferredDeathRecordedEvent recorded)
    {
        var current = Require(state, recorded);
        var night = current.PitHagNight
            ?? throw new InvalidOperationException("事件流顺序损坏：没有麻脸巫婆之夜的窗口，却记了一条待定死亡");
        if (night.Deferred.Any(deferred => deferred.Target == recorded.Target))
        {
            throw new InvalidOperationException($"事件流顺序损坏：席位 {recorded.Target.Value} 已经有一条待定死亡");
        }

        return current with
        {
            PitHagNight = night with
            {
                Deferred =
                [
                    .. night.Deferred,
                    new DeferredDeath
                    {
                        Target = recorded.Target,
                        Source = recorded.Source,
                        Ability = recorded.Ability,
                        Note = recorded.Note,
                        Transformation = recorded.Transformation,
                        Retention = recorded.Retention,
                    },
                ],
            },
        };
    }

    /// <summary>裁定（或窗口收口）一条待定死亡：把它从待定表里移除。</summary>
    private static StepMachineState ApplyDeferredDeathResolved(
        StepMachineState? state,
        DeferredDeathResolvedEvent resolved)
    {
        var current = Require(state, resolved);
        var night = current.PitHagNight
            ?? throw new InvalidOperationException("事件流顺序损坏：没有麻脸巫婆之夜的窗口，却裁定了待定死亡");
        if (!night.Deferred.Any(deferred => deferred.Target == resolved.Target))
        {
            throw new InvalidOperationException($"事件流顺序损坏：席位 {resolved.Target.Value} 没有待定的死亡");
        }

        return current with
        {
            PitHagNight = night with
            {
                Deferred = [.. night.Deferred.Where(deferred => deferred.Target != resolved.Target)],
            },
        };
    }

    /// <summary>关闭窗口：清空状态（关闭前必须先把它名下的待定死亡收口）。</summary>
    private static StepMachineState ApplyPitHagNightClosed(StepMachineState? state, PitHagNightClosedEvent closed)
    {
        var current = Require(state, closed);
        if (current.PitHagNight is null)
        {
            throw new InvalidOperationException("事件流顺序损坏：麻脸巫婆之夜的窗口没有开启，却要关闭");
        }

        return current with { PitHagNight = null };
    }

    /// <summary>
    /// 落下方古的「限一次」标记；一局只能落下一次（重复即事件流损坏——标记整局不复用）。
    /// </summary>
    private static StepMachineState ApplyFangGuInfection(
        StepMachineState? state,
        FangGuInfectionRecordedEvent recorded)
    {
        var current = Require(state, recorded);
        if (current.FangGuInfection is not null)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：方古的「限一次」标记已经落下"
                + $"（{current.FangGuInfection.Source.Value} 号 → {current.FangGuInfection.Seat.Value} 号），"
                + "不能再次侵染");
        }

        return current with
        {
            FangGuInfection = new FangGuInfection
            {
                Seat = recorded.Seat,
                Source = recorded.Source,
                Note = $"方古侵染：{recorded.Source.Value} 号方古把 {recorded.Seat.Value} 号外来者"
                    + "变成新的邪恶方古（限一次标记放置于魔典中心，整局保留）",
            },
        };
    }

    private static StepMachineState ApplyAdvance(StepMachineState? state, int fromIndex, int toIndex)
    {
        var current = state
            ?? throw new InvalidOperationException("事件流顺序损坏：推进事件之前没有状态");

        if (fromIndex != current.SlotIndex)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：推进起点 {fromIndex} 与当前槽位 {current.SlotIndex} 不一致");
        }

        if (toIndex < fromIndex || toIndex > current.Plan.Slots.Count)
        {
            throw new InvalidOperationException($"事件流顺序损坏：推进目标 {toIndex} 越界");
        }

        var next = current with
        {
            SlotIndex = toIndex,
            Quota = SlotQuotaState.Running,
            PendingRequest = null,
            AwaitingDecision = null,
            AwaitingDecisionTriggerAbility = null,
            AwaitingDecisionSeat = null,
            Block = null,

            // 推进到下一格：遍次与「本格已结算」都清零——下一格由它自己的 SlotEnteredEvent 记首次进入。
            SlotPass = 0,
            SlotAbilityResolved = false,
        };

        // 白天计划走完 = 白天结束：必须先有 DayClosedEvent（CloseDay 或强推兜底产出），
        // 否则事件流顺序损坏——恢复必须显式失败，不允许"阶段结束了、白天账还开着"。
        if (toIndex >= current.Plan.Slots.Count
            && current.Plan.Phase == GamePhase.Day
            && next.Day?.OpenDay is { } openDay)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {openDay.DayNumber} 的计划走完，却没有结束白天的事件");
        }

        return next;
    }

    /// <summary>把一条白天事件折进白天账（状态本身由 Require 保证已开始）。</summary>
    private static StepMachineState ApplyDay(StepMachineState? state, GameEvent gameEvent)
    {
        var current = Require(state, gameEvent);
        return current with { Day = DayLedgerFolder.Apply(current.Day, gameEvent) };
    }

    /// <summary>写入胜负结论；一局只能结束一次，重复即事件流损坏（R-0024）。</summary>
    private static StepMachineState ApplyGameEnded(StepMachineState? state, GameEndedEvent ended)
    {
        var current = Require(state, ended);
        if (current.Outcome is not null)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：本局已经以「{current.Outcome.Condition}」结束，不能再次结束");
        }

        return current with
        {
            Outcome = new GameOutcome
            {
                Winner = ended.Winner,
                Condition = ended.Condition,
                Detail = ended.Detail,
            },
        };
    }
}
