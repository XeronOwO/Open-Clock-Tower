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
                BarberNight = CarryBarberNight(state),
            },
            SlotEnteredEvent entered => Require(state, entered) with
            {
                SlotIndex = entered.SlotIndex,
                Quota = SlotQuotaState.Running,
                PendingRequest = null,
                AwaitingDecision = null,
                Block = null,
            },
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
            SlotActivatedEvent activated => ApplySlotActivation(state, activated),

            // 麻脸巫婆之夜的死亡裁量窗口（R-0030）。
            PitHagNightOpenedEvent opened => ApplyPitHagNightOpened(state, opened),
            DeferredDeathRecordedEvent recorded => ApplyDeferredDeathRecorded(state, recorded),
            DeferredDeathResolvedEvent deferredResolved => ApplyDeferredDeathResolved(state, deferredResolved),
            PitHagNightClosedEvent closed => ApplyPitHagNightClosed(state, closed),

            // 「今晚理发」事实（R-0033）：开启 / 关闭改步骤机状态；跳过事件只在事件流里留痕。
            BarberNightOpenedEvent barberOpened => ApplyBarberNightOpened(state, barberOpened),
            BarberNightClosedEvent barberClosed => ApplyBarberNightClosed(state, barberClosed),
            BarberNightSkippedEvent => state,

            // 方古的「限一次」标记（R-0034）：整局事实，落下后不再重复。
            FangGuInfectionRecordedEvent infection => ApplyFangGuInfection(state, infection),

            SeatStateChangedEvent => state,
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
            VoteCountedEvent => ApplyDay(state, gameEvent),

            // 夜晚处罚处决（DayNumber = null）不写白天账：不把"还没有白天"物化成空账（R-0020）。
            ExecutedEvent { DayNumber: null } => Require(state, gameEvent),
            ExecutedEvent => ApplyDay(state, gameEvent),
            DayClosedEvent => ApplyDay(state, gameEvent),

            // 胜负结论与呆瓜选择账：两条都是步骤机自己的账（R-0024 / R-0027）。
            GameEndedEvent ended => ApplyGameEnded(state, ended),
            KlutzChoiceMadeEvent choice => ApplyKlutzChoice(state, choice),
            KlutzChoiceSkippedEvent skipped => ApplyKlutzChoiceSkipped(state, skipped),

            // 状态账的事件：进同一条事件流，但步骤机状态不由它们改变
            // （座位状态变化对步骤机的影响是"作废依赖失效的挂起请求"，在 Handle 阶段已经处理完）。
            // 它们可以**先于任何阶段**出现（开局分配与初始状态）——此时步骤机保持"尚未开始"（null）：
            // 账是同一事件流上的独立派生视图（D-0015），不允许账事件把 null 变成"已开始"。
            PersistentEffectAppliedEvent => state,
            PersistentEffectTerminatedEvent => state,
            InstantaneousEffectAppliedEvent => state,
            MadnessRequirementIssuedEvent => state,
            MadnessRequirementTerminatedEvent => state,
            AbilityResolvedEvent => state,
            InformationResultIssuedEvent => state,

            _ => throw new InvalidOperationException($"未知事件类型：{gameEvent.GetType().Name}"),
        };
    }

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

    private static StepMachineState Require(StepMachineState? state, GameEvent gameEvent) =>
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

        return current with { AwaitingDecision = null };
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
    /// 把一次槽位绑定折进计划：空槽位 → 激活成行动槽位；行动槽位 → 换手重绑（换行动者与提示）。
    /// 只有**尚未进入**的槽位可以被处理（过时不候，《隐性规则汇总》§6）。
    /// </summary>
    /// <remarks>
    /// 顺序损坏一律显式抛错：处理一个已经走过的槽位、下标越界、槽位标识对不上、
    /// 行动槽位重复绑定到同一行动者、或者绑定的不是角色槽位，都是事件流损坏
    /// （恢复必须失败，不许静默继续）。
    /// </remarks>
    private static StepMachineState ApplySlotActivation(StepMachineState? state, SlotActivatedEvent activated)
    {
        var current = Require(state, activated);
        if (activated.SlotIndex <= current.SlotIndex)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 已经进入过（当前下标 {current.SlotIndex}），不能再激活");
        }

        if (activated.SlotIndex >= current.Plan.Slots.Count)
        {
            throw new InvalidOperationException($"事件流顺序损坏：激活下标 {activated.SlotIndex} 越界");
        }

        var slot = current.Plan.Slots[activated.SlotIndex];
        if (slot.Id != activated.SlotId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：下标 {activated.SlotIndex} 是槽位 {slot.Id.Value}，不是 {activated.SlotId.Value}");
        }

        var slots = current.Plan.Slots.ToArray();
        slots[activated.SlotIndex] = slot.Kind switch
        {
            StepSlotKind.Action when slot.Actor == activated.Actor => throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 的行动者没有变化，不能重复绑定"),

            // 换手重绑 / 激活：行动者与提示按新行动者重建，角色归属不变（R-0032）。
            // 行动者本人角色与槽位角色不同时（哲学家代行被获得角色的能力）构造代行槽位（R-0036）。
            StepSlotKind.Action or StepSlotKind.Empty => Rebind(slot, activated),

            _ => throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 不是角色槽位，不能被激活"),
        };

        return current with { Plan = current.Plan with { Slots = slots } };
    }

    /// <summary>
    /// 把一个角色槽位重新绑定给新行动者：行动者本人角色与槽位角色不同（哲学家代行被获得角色的能力，
    /// R-0036）时构造**代行槽位**，否则是普通的行动槽位（R-0032 的换手重绑 / 空槽位激活）。
    /// </summary>
    private static StepSlot Rebind(StepSlot slot, SlotActivatedEvent activated) =>
        activated.ActorCharacter is { } actorCharacter
        && slot.Character is { } slotCharacter
        && actorCharacter != slotCharacter
            ? StepSlot.GrantedAction(
                slot.Id,
                activated.Actor,
                actorCharacter,
                activated.Prompt,
                activated.Dependencies,
                slotCharacter)
            : StepSlot.Action(
                slot.Id,
                activated.Actor,
                activated.Prompt,
                activated.Dependencies,
                slot.Character);

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
        var current = Require(state, raised) with { AwaitingDecision = raised.DecisionPoint };
        if (raised.SlotPrompt is not { } prompt)
        {
            return current;
        }

        var slot = current.CurrentSlot;
        if (slot is null || slot.Id != raised.SlotId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：裁定点 {raised.SlotId.Value} 不在当前槽位上，不能回写槽位提示");
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
    /// 「今晚理发」事实跨阶段保留的守卫：只允许白天 → 夜晚（白天死亡的事件在**当夜**交互，
    /// 百科《死亡触发能力》· 2026-10-01 抓取 · 能力简介）；从夜晚带进新阶段说明夜末的
    /// 「过时不候」收口缺失，显式失败而不是静默顺延（D-0014 能力 3）。
    /// </summary>
    private static BarberNight? CarryBarberNight(StepMachineState? state)
    {
        if (state?.BarberNight is not { } night)
        {
            return null;
        }

        if (state.Plan.Phase is GamePhase.FirstNight or GamePhase.OtherNight)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：夜晚（{state.Plan.Label}）已经结束，理发师之夜事实还没有收口"
                + "（过时不候的关闭事件缺失），不能把它顺延到新阶段");
        }

        return night;
    }

    /// <summary>开启「今晚理发」事实；同一夜不能开两次（重复即事件流损坏）。</summary>
    private static StepMachineState ApplyBarberNightOpened(StepMachineState? state, BarberNightOpenedEvent opened)
    {
        var current = Require(state, opened);
        if (current.BarberNight is not null)
        {
            throw new InvalidOperationException("事件流顺序损坏：理发师之夜事实已经开启过，不能重复开启");
        }

        return current with
        {
            BarberNight = new BarberNight { Source = opened.Source, Note = opened.Note },
        };
    }

    /// <summary>关闭「今晚理发」事实：没有开启却要关闭一律抛错（恢复必须失败，不静默继续）。</summary>
    private static StepMachineState ApplyBarberNightClosed(StepMachineState? state, BarberNightClosedEvent closed)
    {
        var current = Require(state, closed);
        if (current.BarberNight is null)
        {
            throw new InvalidOperationException("事件流顺序损坏：理发师之夜事实没有开启，却要关闭");
        }

        return current with { BarberNight = null };
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
            Block = null,
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

    /// <summary>记录呆瓜的选择；同一名呆瓜只能有一条记录。</summary>
    private static StepMachineState ApplyKlutzChoice(StepMachineState? state, KlutzChoiceMadeEvent choice)
    {
        var current = Require(state, choice);
        if (current.KlutzChoices.Any(record => record.Klutz == choice.Klutz))
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：席位 {choice.Klutz.Value} 的呆瓜选择已经记录过");
        }

        return current with
        {
            KlutzChoices =
            [
                .. current.KlutzChoices,
                new KlutzChoiceRecord
                {
                    Klutz = choice.Klutz,
                    Target = choice.Target,
                    Detail = $"呆瓜（{choice.Klutz.Value} 号）公开选择了 {choice.Target.Value} 号",
                },
            ],
        };
    }

    /// <summary>记录呆瓜"没有选择"（能力未生效 / 被作废）；同一名呆瓜只能有一条记录。</summary>
    private static StepMachineState ApplyKlutzChoiceSkipped(
        StepMachineState? state,
        KlutzChoiceSkippedEvent skipped)
    {
        var current = Require(state, skipped);
        if (current.KlutzChoices.Any(record => record.Klutz == skipped.Klutz))
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：席位 {skipped.Klutz.Value} 的呆瓜选择已经记录过");
        }

        return current with
        {
            KlutzChoices =
            [
                .. current.KlutzChoices,
                new KlutzChoiceRecord
                {
                    Klutz = skipped.Klutz,
                    Detail = skipped.Reason,
                },
            ],
        };
    }
}
