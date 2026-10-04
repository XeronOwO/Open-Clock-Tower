namespace OpenClockTower.Kernel;

/// <summary>
/// 行动槽位的结算调度：把「玩家已经选完 / 说书人已经裁完」翻译成结算事件。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 拆出：步骤机管推进与挂起，这里只管"这一步做完了该产出什么"。
/// 结算契约按槽位记录的 <see cref="StepSlot.Owner"/> 从注入目录取——计划里不带行为对象，
/// 保证计划能随事件 JSON 往返（快照也是 JSON）。
/// </para>
/// <para>
/// 生效判定在结算时刻做，取的是**当时的账**（R-0004）：中毒 / 醉酒 / 死亡 → 不生效；
/// 判不了就返回判不了，由调用方拒绝整条命令（不猜，D-0015）。
/// </para>
/// </remarks>
internal static class AbilitySettlement
{
    /// <summary>尝试为一个已经完成选择的行动槽位安排结算。</summary>
    /// <param name="slot">当前槽位。</param>
    /// <param name="state">步骤机状态（提供计划标识与阶段）。</param>
    /// <param name="context">结算上下文（账 / 座次 / 契约目录）。</param>
    /// <param name="choice">玩家的选择值；入口裁定点直接结算时为 null。</param>
    /// <param name="decision">说书人的裁定原文；玩家选择后结算时为 null。</param>
    internal static AbilitySettlementPlan Plan(
        StepSlot slot,
        StepMachineState state,
        SettlementContext context,
        string? choice,
        string? decision)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (slot.Actor is not { } actor || slot.Owner is not { } owner)
        {
            return AbilitySettlementPlan.NoContract;
        }

        var ability = context.Abilities.Find(owner);
        if (ability is null)
        {
            return AbilitySettlementPlan.NoContract;
        }

        var entry = context.State.Seat(actor);
        var outcome = entry is null ? null : AbilityEffectivenessEvaluator.Evaluate(context.State, entry);
        if (outcome is null)
        {
            return AbilitySettlementPlan.Indeterminate(
                $"无法判定席位 {actor.Value} 的能力是否生效：该席位的生死 / 醉酒 / 中毒还没有观测齐，"
                + "结算不替它猜（D-0015）");
        }

        var resolutionContext = new AbilityResolutionContext
        {
            SlotId = slot.Id,
            PlanLabel = state.Plan.Label,
            Phase = state.Plan.Phase,
            Actor = actor,
            ActorCharacter = owner,
            ActorOwnCharacter = slot.Character ?? owner,
            Seats = context.Seats,
            State = context.State,
            Outcome = outcome,
            Choice = choice,
            Decision = decision,
            DaysStarted = state.Day?.Days.Count ?? 0,
            LastDay = state.Day?.Days.LastOrDefault(),
            Plan = state.Plan,
            SlotIndex = state.SlotIndex,
            SlotPass = state.SlotPass,
            PitHagNightActive = state.PitHagNight is not null,
            FangGuInfectionConsumed = state.FangGuInfection is not null,
        };

        // 玩家选完、说书人还没裁：先问是否需要再裁定一次（信息类能力要把信息内容交给说书人）。
        if (decision is null)
        {
            var prompt = ability.BuildPostChoiceDecision(resolutionContext);
            if (prompt is not null)
            {
                return AbilitySettlementPlan.RequiresDecision(prompt);
            }
        }

        var events = new List<GameEvent>();

        // 摇头 / 不用（哲学家、女裁缝）不算使用：不记「用过」、也不记失效（R-0036 / R-0040）——
        // 记了会让建表期误判"机会已浪费"，把之后的夜晚一并吞掉。
        if (ability.CountsAsUse(resolutionContext))
        {
            events.Add(new AbilityResolvedEvent
            {
                SlotId = slot.Id,
                Actor = actor,
                Ability = ability.Ability,
                Effective = outcome.Effective,
                Malfunctions = [.. outcome.Malfunctions, .. ability.InterferenceMalfunctions(resolutionContext)],
                Note = outcome.Note,
            });
        }

        events.AddRange(ability.Resolve(resolutionContext));
        return AbilitySettlementPlan.Resolved(events);
    }

    /// <summary>
    /// 裁定点的稳定标识：与「槽位的这一次进入」一对一（入口裁定与选择后裁定不会共存；
    /// 重进的遍次带 <c>#N</c>——两次进入各自有各自的身份，R-0052 第 2 条）。
    /// </summary>
    internal static DecisionPointId DecisionPointIdOf(StepMachineState state, StepSlot slot) =>
        new($"{StepSlotEntry.SlotKeyOf(state, slot)}:decision");
}
