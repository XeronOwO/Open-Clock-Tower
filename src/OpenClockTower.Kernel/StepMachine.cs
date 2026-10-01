namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机：驱动阶段与夜晚顺序表推进、生成操作请求与裁定点，并能在挂起点暂停。
/// </summary>
/// <remarks>
/// <para>
/// **纯计算**（D-0008）：给定（状态 + 输入）必产出同一结果。时间不是内核的输入来源——
/// 配额走完、座位状态变化都是上层送进来的输入；内核只产出事件。
/// </para>
/// <para>
/// **挂起语义**（D-0011 / D-0013）：槽位配额是**最短**时间，自动推进需要两个条件同时满足——
/// 配额已走完（<see cref="SlotQuotaState.Elapsed"/>）且没有挂起。请求没有超时：
/// 玩家一直不响应，槽位就一直不推进（玩家响应慢会拉长夜晚，属 D-0013 已登记的边界）。
/// </para>
/// <para>
/// **兜底语义**（D-0014）：<see cref="ForceAdvanceInput"/> 越过配额与挂起直接推进，
/// <see cref="TakeOverInput"/> 暂停自动推进；每一次越过都产出事件、可审计、可重放。
/// 挂起、阻塞、逻辑出错都不构成死锁——兜底入口永远开着。
/// </para>
/// <para>
/// <see cref="Handle"/> 与 <see cref="Apply"/> 成对：前者产出事件，后者把事件折叠回状态；
/// 测试断言两者一致，重放（重启恢复、断线补齐）因此可信。
/// </para>
/// </remarks>
public static class StepMachine
{
    /// <summary>开启一个新阶段：按计划进入第一个槽位，必要时发出操作请求或裁定点。</summary>
    public static StepMachineOutcome StartPhase(StepPlan plan, ControlMode control = ControlMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var events = new List<GameEvent>(capacity: 4)
        {
            new PhaseStartedEvent { Plan = plan, Control = control },
        };
        var state = Apply(null, events[0]);
        if (!state.IsPlanCompleted)
        {
            EnterCurrentSlot(state, events);
        }
        else
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = plan.Label });
        }

        return AppliedFromNothing(events);
    }

    /// <summary>处理一条输入；非法输入被拒绝而不抛异常。</summary>
    public static StepMachineOutcome Handle(StepMachineState state, StepMachineInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        return input switch
        {
            SlotQuotaElapsedInput => HandleQuotaElapsed(state),
            SubmitResponseInput response => HandleResponse(state, response),
            VoidRequestInput voidRequest => HandleVoid(state, voidRequest),
            ForceAdvanceInput forceAdvance => HandleForceAdvance(state, forceAdvance),
            TakeOverInput takeOver => HandleControlChange(state, ControlMode.StorytellerTakeover, takeOver.Reason),
            ReleaseControlInput release => HandleControlChange(state, ControlMode.Automatic, release.Reason),
            SeatStateChangedInput seatChanged => HandleSeatStateChanged(state, seatChanged),
            ResolveDecisionPointInput resolve => HandleDecisionResolved(state, resolve),
            _ => Reject(state, StepMachineRejectionReason.UnexpectedInput, $"未知输入：{input.GetType().Name}"),
        };
    }

    /// <summary>
    /// 把一条事件折叠回状态——重放、重启恢复、撤销的共同基础。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 事件流顺序损坏时抛出：恢复必须**显式失败**，绝不静默继续（D-0014 能力 3）。
    /// </exception>
    public static StepMachineState Apply(StepMachineState? state, GameEvent gameEvent)
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
            SeatStateChangedEvent seatChanged => Require(state, seatChanged),
            DecisionPointRaisedEvent raised => Require(state, raised) with
            {
                AwaitingDecision = raised.DecisionPoint,
            },
            DecisionPointResolvedEvent resolved => ResolveDecision(state, resolved),
            SlotBlockedEvent blocked => Require(state, blocked) with
            {
                Block = new StepBlock { Reason = blocked.Reason },
            },

            // 状态账的事件：进同一条事件流，但步骤机状态不由它们改变
            // （座位状态变化对步骤机的影响是"作废依赖失效的挂起请求"，在 Handle 阶段已经处理完）。
            PersistentEffectAppliedEvent => Require(state, gameEvent),
            PersistentEffectTerminatedEvent => Require(state, gameEvent),
            InstantaneousEffectAppliedEvent => Require(state, gameEvent),
            MadnessRequirementIssuedEvent => Require(state, gameEvent),

            _ => throw new InvalidOperationException($"未知事件类型：{gameEvent.GetType().Name}"),
        };
    }

    /// <summary>从事件流重建状态（重放、重启恢复、撤销的基础）。</summary>
    public static StepMachineState Fold(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return ApplyAll(null, events);
    }

    private static StepMachineOutcome HandleQuotaElapsed(StepMachineState state)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        if (state.Quota == SlotQuotaState.Elapsed)
        {
            return Applied(state, []);
        }

        var events = new List<GameEvent>
        {
            new SlotQuotaElapsedEvent { SlotId = state.CurrentSlot!.Id },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleResponse(StepMachineState state, SubmitResponseInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        var pending = state.PendingRequest;
        if (pending is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingRequest, "当前没有等待响应的请求");
        }

        if (pending.Id != input.RequestId)
        {
            return Reject(state, StepMachineRejectionReason.NotCurrentRequest, $"当前挂起的是 {pending.Id}");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            return Reject(state, StepMachineRejectionReason.RequestAlreadyResolved, "请求已经了结");
        }

        if (!pending.Prompt.Options.Any(option => string.Equals(option.Value, input.OptionValue, StringComparison.Ordinal)))
        {
            return Reject(state, StepMachineRejectionReason.OptionNotLegal, $"选项不在合法集合里：{input.OptionValue}");
        }

        var events = new List<GameEvent>
        {
            new OperationRequestAnsweredEvent
            {
                RequestId = pending.Id,
                Answer = new OperationRequestAnswer
                {
                    OptionValue = input.OptionValue,
                    Source = input.Source,
                    Note = input.Note,
                },
            },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleVoid(StepMachineState state, VoidRequestInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        var pending = state.PendingRequest;
        if (pending is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingRequest, "当前没有等待响应的请求");
        }

        if (pending.Id != input.RequestId)
        {
            return Reject(state, StepMachineRejectionReason.NotCurrentRequest, $"当前挂起的是 {pending.Id}");
        }

        if (pending.Status != OperationRequestStatus.Pending)
        {
            return Reject(state, StepMachineRejectionReason.RequestAlreadyResolved, "请求已经了结");
        }

        var events = new List<GameEvent>
        {
            new OperationRequestVoidedEvent
            {
                RequestId = pending.Id,
                Void = new OperationRequestVoid { Reason = input.Reason, Note = input.Note },
            },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleForceAdvance(StepMachineState state, ForceAdvanceInput input)
    {
        if (state.IsPlanCompleted)
        {
            return Reject(state, StepMachineRejectionReason.PlanAlreadyCompleted, "本计划已走完");
        }

        var events = new List<GameEvent>();
        if (state.PendingRequest is { Status: OperationRequestStatus.Pending } pending)
        {
            events.Add(new OperationRequestVoidedEvent
            {
                RequestId = pending.Id,
                Void = new OperationRequestVoid
                {
                    Reason = OperationRequestVoidReason.StorytellerTakeover,
                    Note = input.Reason,
                },
            });
        }

        if (state.AwaitingDecision is { } decision)
        {
            events.Add(new DecisionPointResolvedEvent
            {
                DecisionPointId = decision.Id,
                Decision = null,
                Note = $"强推：{input.Reason}",
            });
        }

        var afterHolds = ApplyAll(state, events);
        AppendForceAdvance(afterHolds, events, input.Reason);
        return Applied(state, events);
    }

    private static StepMachineOutcome HandleControlChange(StepMachineState state, ControlMode mode, string reason)
    {
        if (state.Control == mode)
        {
            return Reject(state, StepMachineRejectionReason.ControlModeUnchanged, $"控制模式已经是 {mode}");
        }

        var events = new List<GameEvent>
        {
            new ControlModeChangedEvent { Mode = mode, Reason = reason },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static StepMachineOutcome HandleSeatStateChanged(StepMachineState state, SeatStateChangedInput input)
    {
        if (input.Life is null
            && input.Character is null
            && input.Alignment is null
            && input.Drunk is null
            && input.Poison is null)
        {
            return Reject(
                state,
                StepMachineRejectionReason.UnexpectedInput,
                "座位状态变化至少要给出一个观测维度");
        }

        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                Seat = input.Seat,
                Life = input.Life,
                Character = input.Character,
                Alignment = input.Alignment,
                Drunk = input.Drunk,
                Poison = input.Poison,
                Reason = input.Reason,
                CausedBy = input.CausedBy,
            },
        };

        var pending = state.PendingRequest;
        if (pending is null || pending.Status != OperationRequestStatus.Pending)
        {
            return Applied(state, events);
        }

        var violated = pending.Dependencies.FirstOrDefault(dependency => IsViolated(dependency, input));
        if (violated is null)
        {
            return Applied(state, events);
        }

        events.Add(new OperationRequestVoidedEvent
        {
            RequestId = pending.Id,
            Void = new OperationRequestVoid
            {
                Reason = OperationRequestVoidReason.DependencyViolated,
                Note = DescribeViolation(violated, input),
            },
        });
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static bool IsViolated(SeatDependency dependency, SeatStateChangedInput input) =>
        dependency.Seat == input.Seat
        && ((dependency.RequiredLife is { } requiredLife && input.Life is { } life && life != requiredLife)
            || (dependency.RequiredCharacter is { } requiredCharacter
                && input.Character is { } character
                && character != requiredCharacter));

    private static string DescribeViolation(SeatDependency dependency, SeatStateChangedInput input)
    {
        var parts = new List<string>();
        if (input.Life is { } life && dependency.RequiredLife is { } requiredLife && life != requiredLife)
        {
            parts.Add($"生死 {life} ≠ 要求 {requiredLife}");
        }

        if (input.Character is { } character
            && dependency.RequiredCharacter is { } requiredCharacter
            && character != requiredCharacter)
        {
            parts.Add($"角色 {character} ≠ 要求 {requiredCharacter}");
        }

        return $"座位 {dependency.Seat} 的状态变化使请求失去意义（{string.Join("；", parts)}）";
    }

    private static StepMachineOutcome HandleDecisionResolved(StepMachineState state, ResolveDecisionPointInput input)
    {
        if (state.AwaitingDecision is null)
        {
            return Reject(state, StepMachineRejectionReason.NoPendingDecision, "当前没有等待说书人的裁定点");
        }

        if (state.AwaitingDecision.Id != input.DecisionPointId)
        {
            return Reject(
                state,
                StepMachineRejectionReason.NotCurrentRequest,
                $"当前挂起的是 {state.AwaitingDecision.Id}");
        }

        var events = new List<GameEvent>
        {
            new DecisionPointResolvedEvent
            {
                DecisionPointId = input.DecisionPointId,
                Decision = input.Decision,
                Note = input.Note,
            },
        };
        return Applied(state, WithAutoAdvance(state, events));
    }

    private static List<GameEvent> WithAutoAdvance(StepMachineState state, List<GameEvent> events)
    {
        var after = ApplyAll(state, events);
        if (CanAutoAdvance(after))
        {
            AppendAdvance(after, events);
        }

        return events;
    }

    private static bool CanAutoAdvance(StepMachineState state) =>
        !state.IsPlanCompleted
        && state.Control == ControlMode.Automatic
        && state.Quota == SlotQuotaState.Elapsed
        && !state.IsHeld;

    private static void AppendAdvance(StepMachineState state, List<GameEvent> events)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotAdvancedEvent { FromIndex = from, ToIndex = to });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        EnterCurrentSlot(Apply(state, events[^1]), events);
    }

    private static void AppendForceAdvance(StepMachineState state, List<GameEvent> events, string reason)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotForceAdvancedEvent { FromIndex = from, ToIndex = to, Reason = reason });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        EnterCurrentSlot(Apply(state, events[^1]), events);
    }

    private static void EnterCurrentSlot(StepMachineState state, List<GameEvent> events)
    {
        var slot = state.CurrentSlot
            ?? throw new InvalidOperationException("进入槽位失败：计划已走完");

        events.Add(new SlotEnteredEvent { SlotIndex = state.SlotIndex, SlotId = slot.Id });

        if (slot.Kind != StepSlotKind.Action)
        {
            return;
        }

        if (slot.Actor is null || slot.Prompt is null)
        {
            events.Add(new SlotBlockedEvent
            {
                SlotId = slot.Id,
                Reason = "行动槽位缺少行动者或选择契约（数据缺陷）——说书人可强推 / 接管 / 重建",
            });
            return;
        }

        switch (slot.Prompt.Evaluate())
        {
            case DecisionPointOutcome.AwaitingChoice:
                events.Add(new OperationRequestIssuedEvent { Request = BuildRequest(state, slot) });
                break;
            case DecisionPointOutcome.Skipped:
                events.Add(new PromptSkippedEvent
                {
                    SlotId = slot.Id,
                    Reason = "无合法选项：按声明的 Skip 走（R-0009），配额照走",
                });
                break;
            case DecisionPointOutcome.StorytellerDecides:
                events.Add(new DecisionPointRaisedEvent
                {
                    SlotId = slot.Id,
                    DecisionPoint = new DecisionPoint
                    {
                        Id = new DecisionPointId($"{state.Plan.Label}:{slot.Id}:decision"),
                        Prompt = slot.Prompt,
                    },
                });
                break;
            default:
                events.Add(new SlotBlockedEvent { SlotId = slot.Id, Reason = slot.Prompt.Context });
                break;
        }
    }

    private static OperationRequest BuildRequest(StepMachineState state, StepSlot slot) =>
        new()
        {
            Id = new OperationRequestId($"{state.Plan.Label}:{slot.Id}"),
            Addressee = slot.Actor!.Value,
            SlotId = slot.Id,
            PlanLabel = state.Plan.Label,
            IssuedAtSlotIndex = state.SlotIndex,
            Prompt = slot.Prompt!,
            Dependencies = slot.Dependencies,
        };

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = ApplyAll(state, events),
            Events = events,
        };

    private static StepMachineOutcome AppliedFromNothing(List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = ApplyAll(null, events),
            Events = events,
        };

    private static StepMachineOutcome Reject(
        StepMachineState state,
        StepMachineRejectionReason reason,
        string note) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Rejected,
            State = state,
            Events = [],
            RejectionReason = reason,
            RejectionNote = note,
        };

    private static StepMachineState ApplyAll(StepMachineState? state, IEnumerable<GameEvent> events)
    {
        var current = state;
        foreach (var gameEvent in events)
        {
            current = Apply(current, gameEvent);
        }

        return current ?? throw new InvalidOperationException("事件流为空：无法从空事件重建状态");
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

        return current with
        {
            SlotIndex = toIndex,
            Quota = SlotQuotaState.Running,
            PendingRequest = null,
            AwaitingDecision = null,
            Block = null,
        };
    }
}
