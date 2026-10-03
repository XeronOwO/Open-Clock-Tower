using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 理发师的死亡触发：以理发师身份死亡且未醉酒 / 未中毒时记「今晚理发」事实；
/// 当晚理发师格进入时由 <see cref="BarberSwapInteraction"/> 完成「两名玩家交换角色（或放弃）」的交互。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为百科 · 2026-10-01 抓取）：《理发师》· 角色能力——「如果你死亡，在当晚恶魔可以选择两名玩家
/// （不能选择其他恶魔）交换角色」；· 提示标记「今晚理发」——放置条件「理发师死亡且此时未醉酒中毒」、
/// 移除时机「能力触发、恶魔执行交换（或放弃）后」；· 角色简介——「必须作为理发师死亡才触发」
/// （死亡之后才变成理发师不触发）；《死亡触发能力》· 能力简介——死亡时立即触发，
/// 涉及交互的效果等到夜晚。平台口径见 <c>docs/standard/rulings.md</c> R-0033。
/// </para>
/// <para>
/// 本类只做两件事：① 把死亡事件翻译成「开事实 / 显式跳过」；② 把理发师格进入、裁定、应答、作废
/// 四类事件路由给交互实现。事实的机器级生命周期在 <see cref="StepMachineState.BarberNight"/>
/// 与 <see cref="StepSlotEntry"/> 的夜晚收口里。
/// </para>
/// <para>
/// **幂等**：事实开启读步骤机状态与本批已产出事件；交互只开一次（由
/// <see cref="BarberSwapInteraction.TryOpen"/> 的挂起 / 等待裁定 / 本批已产出三重判据保证）。
/// </para>
/// </remarks>
internal sealed class BarberNightTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => BarberAbility.SwapAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var events = new List<GameEvent>();

        // 本批之前事实是否已挂着；死亡处理开出后同步置位，供同一批里排在后面的理发师格使用。
        var factActive = context.Machine?.BarberNight is not null;
        var interactionHandled = false;

        for (var index = 0; index < context.Events.Count; index++)
        {
            switch (context.Events[index])
            {
                case SeatStateChangedEvent { Life: LifeState.Dead } death:
                    HandleDeath(context, death, index, events, ref interactionHandled);
                    factActive |= events.OfType<BarberNightOpenedEvent>().Any();
                    break;

                case SlotEnteredEvent entered:
                    if (factActive && IsBarberTriggerSlot(context, entered.SlotIndex))
                    {
                        BarberSwapInteraction.TryOpen(context, events, ref interactionHandled);
                    }

                    break;

                case DecisionPointResolvedEvent resolved:
                    BarberSwapInteraction.HandleDemonChosen(context, resolved, events, ref interactionHandled);
                    break;

                case OperationRequestAnsweredEvent answered:
                    BarberSwapInteraction.HandleAnswer(context, answered, events);
                    break;

                case OperationRequestVoidedEvent voided:
                    BarberSwapInteraction.HandleVoid(context, voided, events);
                    break;
            }
        }

        return events;
    }

    /// <summary>死亡事件 → 开事实或显式跳过；死亡就发生在理发师格时同一批把交互补开。</summary>
    private static void HandleDeath(
        EventTriggerContext context,
        SeatStateChangedEvent death,
        int index,
        List<GameEvent> events,
        ref bool interactionHandled)
    {
        if (context.Machine?.BarberNight is not null || events.OfType<BarberNightOpenedEvent>().Any())
        {
            // 同一名理发师只有一次死亡触发；重复观测不产生第二条事实。
            return;
        }

        var (fact, skipReason) = EvaluateDeath(context, death, index);
        if (skipReason is { } reason)
        {
            events.Add(new BarberNightSkippedEvent { Seat = death.Seat, Reason = reason });
            return;
        }

        if (fact is null)
        {
            return;
        }

        events.Add(new BarberNightOpenedEvent { Source = fact.Source, Note = fact.Note });

        // 死亡就发生在理发师格（格子已经进入、交互还没开）→ 同一批补开交互。
        if (context.Machine is { } machine
            && BarberSwapInteraction.TryBarberSlotIndex(machine) is { } slotIndex
            && machine.SlotIndex == slotIndex)
        {
            BarberSwapInteraction.TryOpen(context, events, ref interactionHandled);
        }
    }

    /// <summary>
    /// 对一条死亡事件求值：能开出「今晚理发」时给出事实；不能开时给出**显式的**跳过原因；
    /// 这条死亡不是理发师死亡时两者都为 null（与本触发器无关）。
    /// </summary>
    /// <remarks>交互实现也用它判断「本批即将开出的死亡事实」（同一批里格子在死亡之前的情况）。</remarks>
    internal static (BarberNight? Fact, string? SkipReason) EvaluateDeath(
        EventTriggerContext context,
        SeatStateChangedEvent death,
        int index)
    {
        var diedAsBarber = DeathTriggerReadings.CharacterAt(context, death.Seat, index);
        if (diedAsBarber is null)
        {
            return (null, "死亡批里该席位有角色变化、却缺少「变化前角色」：判不了死亡时是不是理发师，"
                + "不猜也不静默（如确为理发师死亡，请上报角色变化时带上变化前角色）");
        }

        if (diedAsBarber != BarberAbility.Character)
        {
            return (null, null);
        }

        if (context.Machine is not { } machine)
        {
            return (null, "阶段还没有开始（预阶段死亡观测）：理发师之夜交互无法排期，不静默顺延到首夜");
        }

        var entry = context.State.Seat(death.Seat);
        if (entry is null)
        {
            return (null, null);
        }

        // 放置条件：死亡时未醉酒 / 未中毒；未观测 → 判不了就不放（不猜，D-0015）。
        if (entry.DrunkValue != DrunkState.Sober || entry.PoisonValue != PoisonState.Healthy)
        {
            return (null, "理发师死亡时醉酒 / 中毒（或这两维尚未观测）：不放置「今晚理发」，恶魔今晚不能交换角色");
        }

        if (machine.Plan.Phase is GamePhase.FirstNight or GamePhase.OtherNight)
        {
            if (BarberSwapInteraction.TryBarberSlotIndex(machine) is not { } slotIndex)
            {
                return (null, "首夜顺序表上没有理发师格：死亡触发的交互当夜无法进行（过时不候，不顺延到下一夜）");
            }

            if (machine.SlotIndex > slotIndex)
            {
                return (null, "理发师格已经走过：死亡触发的交互当夜不再补开（过时不候，不顺延到下一夜）");
            }
        }

        return (
            new BarberNight
            {
                Source = death.Seat,
                Note = $"理发师（{death.Seat.Value} 号）死亡，死亡时未醉酒 / 未中毒：记「今晚理发」，"
                    + "当夜理发师格由恶魔选择两名玩家交换角色（或放弃）",
            },
            null);
    }

    /// <summary>进入的槽位是不是「理发师触发格」（只有它需要按事实决定是否开交互）。</summary>
    private static bool IsBarberTriggerSlot(EventTriggerContext context, int slotIndex)
    {
        if (context.Machine is not { } machine
            || slotIndex < 0
            || slotIndex >= machine.Plan.Slots.Count)
        {
            return false;
        }

        var slot = machine.Plan.Slots[slotIndex];
        return slot.Kind == StepSlotKind.Trigger && slot.Character == BarberAbility.Character;
    }
}
