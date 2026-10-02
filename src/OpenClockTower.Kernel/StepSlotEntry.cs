namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位的进入与推进：把一个槽位翻译成事件（请求 / 裁定点 / 跳过 / 阻塞），以及推进到下一个槽位。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 拆出（单文件 600 行门禁）：步骤机负责"处理输入、派生后果"，
/// 这里只回答"进入第 k 个槽位要产出什么"——纯事件构造，无状态、无 IO（D-0008）。
/// </para>
/// <para>
/// 空槽位与节拍槽位只产出 <see cref="SlotEnteredEvent"/>（照样消耗配额，D-0013 §1）；
/// 行动槽位按选择契约的求值结果分成请求 / 跳过 / 裁定点 / 阻塞四路（R-0009）。
/// </para>
/// </remarks>
internal static class StepSlotEntry
{
    /// <summary>自动推进条件：计划未完、非白天窗口、自动控制、配额已走完且没有挂起。</summary>
    internal static bool CanAutoAdvance(StepMachineState state) =>
        !state.IsPlanCompleted
        && state.CurrentSlot?.Kind != StepSlotKind.DayWindow
        && state.Control == ControlMode.Automatic
        && state.Quota == SlotQuotaState.Elapsed
        && !state.IsHeld;

    /// <summary>产出一条自动推进事件；推进后进入新槽位（计划走完则补阶段完成事件）。</summary>
    internal static void AppendAdvance(StepMachineState state, List<GameEvent> events)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotAdvancedEvent { FromIndex = from, ToIndex = to });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            events);
    }

    /// <summary>产出一条强推事件（说书人兜底，D-0014）；推进后进入新槽位。</summary>
    internal static void AppendForceAdvance(StepMachineState state, List<GameEvent> events, string reason)
    {
        var from = state.SlotIndex;
        var to = from + 1;
        events.Add(new SlotForceAdvancedEvent { FromIndex = from, ToIndex = to, Reason = reason });
        if (to >= state.Plan.Slots.Count)
        {
            events.Add(new PhaseCompletedEvent { PlanLabel = state.Plan.Label });
            return;
        }

        Enter(
            StepMachineFolder.Apply(state, events[^1])
                ?? throw new InvalidOperationException("事件流损坏：推进后丢失步骤机状态"),
            events);
    }

    /// <summary>进入当前槽位：产出槽位进入事件，并按槽位种类与选择契约派生后续事件。</summary>
    internal static void Enter(StepMachineState state, List<GameEvent> events)
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
                    Reason = $"无合法选项：{slot.Prompt.Context}"
                        + "（按声明的 Skip 走，R-0009；配额照走）",
                });
                break;
            case DecisionPointOutcome.StorytellerDecides:
                events.Add(new DecisionPointRaisedEvent
                {
                    SlotId = slot.Id,
                    DecisionPoint = new DecisionPoint
                    {
                        Id = AbilitySettlement.DecisionPointIdOf(state, slot),
                        Prompt = slot.Prompt,
                    },
                });
                break;
            default:
                events.Add(new SlotBlockedEvent { SlotId = slot.Id, Reason = slot.Prompt.Context });
                break;
        }
    }

    /// <summary>为一个行动槽位构造操作请求（槽位来源：随槽位推进了结、消耗夜晚配额）。</summary>
    private static OperationRequest BuildRequest(StepMachineState state, StepSlot slot) =>
        new()
        {
            Id = new OperationRequestId($"{state.Plan.Label}:{slot.Id}"),
            Addressee = slot.Actor!.Value,
            Origin = OperationRequestOrigin.ForSlot(slot.Id, state.Plan.Label, state.SlotIndex),
            Prompt = slot.Prompt!,
            Dependencies = slot.Dependencies,
        };
}
