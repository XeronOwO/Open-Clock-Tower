using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 贤者的死亡触发：以贤者身份**被恶魔杀死**时记「当晚展示」事实；
/// 当夜贤者格进入时由 <see cref="SageInteraction"/> 开说书人裁定并下发信息。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为百科 · 2026-10-01 抓取）：《贤者》· 角色能力——「如果恶魔杀死了你，在**当晚**你会被
/// 唤醒并得知两名玩家，其中一名是杀死你的那个恶魔」；· 角色简介 2——「贤者只有在被恶魔杀死时才会
/// 获取信息，死于处决时不会」；· 范例 3——死于麻脸巫婆（而非恶魔）时不醒来、不获信息；
/// 《死亡触发能力》· 能力简介——这类能力死亡时立即触发，涉及交互的效果等到夜晚；
/// 《夜晚行动顺序一览》· 其他夜晚——贤者条目。平台口径见 <c>docs/standard/rulings.md</c> R-0038。
/// </para>
/// <para>
/// 本类只做两件事：① 把死亡事件翻译成「开事实 / 显式跳过」；② 把贤者格进入、裁定结清两类事件
/// 路由给交互实现。事实的机器级生命周期在 <see cref="StepMachineState.SageNight"/> 与
/// <see cref="StepSlotEntry"/> 的夜晚收口里。
/// </para>
/// <para>
/// **幂等**：事实开启读步骤机状态与本批已产出事件；交互只开一次（由 <see cref="SageInteraction.TryOpen"/>
/// 的挂起 / 等待裁定 / 本批已产出三重判据保证）。
/// </para>
/// </remarks>
internal sealed class SageNightTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => SageAbility.InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 已经结束：不再开任何展示（《处决》第 3 步先于第 4 步；与呆瓜同族的第二道保险，R-0024）。
        if (context.Machine?.Outcome is not null)
        {
            return [];
        }

        var events = new List<GameEvent>();
        var interactionHandled = false;

        for (var index = 0; index < context.Events.Count; index++)
        {
            switch (context.Events[index])
            {
                case SeatStateChangedEvent { Life: LifeState.Dead } death:
                    HandleDeath(context, death, index, events, ref interactionHandled);
                    break;

                case SlotEnteredEvent entered:
                    if (HasActiveFact(context, events) && IsSageTriggerSlot(context, entered.SlotIndex))
                    {
                        SageInteraction.TryOpen(context, events, ref interactionHandled);
                    }

                    break;

                case DecisionPointResolvedEvent resolved:
                    SageInteraction.HandleResolved(context, resolved, events);
                    break;
            }
        }

        return events;
    }

    /// <summary>死亡事件 → 开事实或显式跳过；死亡就发生在贤者格时同一批把交互补开。</summary>
    private static void HandleDeath(
        EventTriggerContext context,
        SeatStateChangedEvent death,
        int index,
        List<GameEvent> events,
        ref bool interactionHandled)
    {
        if (HasActiveFact(context, events))
        {
            // 同一夜只有一条待处理事实；重复观测不产生第二条。
            return;
        }

        var (fact, skipReason) = EvaluateDeath(context, death, index);
        if (skipReason is { } reason)
        {
            events.Add(new SageNightSkippedEvent { Sage = death.Seat, Reason = reason });
            return;
        }

        if (fact is null)
        {
            return;
        }

        events.Add(new SageNightOpenedEvent
        {
            Sage = fact.Sage,
            Demon = fact.Demon,
            DemonCharacter = fact.DemonCharacter,
            Effective = fact.Effective,
            Note = fact.Note,
        });

        // 死亡就发生在贤者格（格子已经进入、交互还没开）→ 同一批补开交互（与理发师同款防御）。
        if (context.Machine is { } machine
            && SageInteraction.TrySageSlotIndex(machine) is { } slotIndex
            && machine.SlotIndex == slotIndex)
        {
            SageInteraction.TryOpen(context, events, ref interactionHandled);
        }
    }

    /// <summary>
    /// 对一条死亡事件求值：能开出「当晚展示」时给出事实；不能开时给出**显式的**跳过原因；
    /// 这条死亡不是贤者死亡时两者都为 null（与本触发器无关）。
    /// </summary>
    /// <remarks>交互实现也用它判断「本批即将开出的死亡事实」（同一批里格子在死亡之前的情况）。</remarks>
    internal static (SageNight? Fact, string? SkipReason) EvaluateDeath(
        EventTriggerContext context,
        SeatStateChangedEvent death,
        int index)
    {
        var diedAsSage = DeathTriggerReadings.CharacterAt(context, death.Seat, index);
        if (diedAsSage is null)
        {
            // 本批有该席位的角色变化、却缺「变化前角色」：判不了死亡时是不是贤者——
            // 不猜，也不静默（与理发师同族，R-0038）。
            var hasCharacterChange = context.Events.Any(gameEvent =>
                gameEvent is SeatStateChangedEvent { Character: not null } change && change.Seat == death.Seat);
            return hasCharacterChange
                ? (null, "死亡批里该席位有角色变化、却缺少「变化前角色」：判不了死亡时是不是贤者，"
                    + "不猜也不静默（如确为贤者死亡，请上报角色变化时带上变化前角色）")
                : (null, null);
        }

        if (diedAsSage != SageAbility.Character)
        {
            // 不是作为贤者死亡：与本触发器无关。
            return (null, null);
        }

        if (death.CausedBy is not { } demon)
        {
            return (null, "贤者死于非恶魔来源（死亡事件没有击杀者归因，例如处决）：不触发（R-0038 第 1 条）");
        }

        var demonCharacter = DeathTriggerReadings.CharacterAt(context, demon, index);
        if (demonCharacter is not { } killerCharacter)
        {
            return (null, $"贤者的击杀者（{demon.Value} 号）在死亡时刻的角色未观测："
                + "判不了是不是恶魔，不猜、不触发（D-0015）");
        }

        if (SectsAndVioletsRoster.TypeOf(killerCharacter) != CharacterType.Demon)
        {
            return (null, $"贤者的击杀者（{demon.Value} 号）在死亡时刻的角色是 {killerCharacter.Value}，"
                + "不是恶魔：不触发（R-0038 第 1 条；女巫诅咒 / 处罚处决 / 麻脸巫婆追加死亡同族）");
        }

        if (context.Machine is not { } machine)
        {
            return (null, "阶段还没有开始（预阶段死亡观测）：贤者的当晚展示无法排期，不静默顺延");
        }

        if (machine.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
        {
            return (null, $"当前阶段是 {machine.Plan.Phase}，不是夜晚：贤者的展示只发生在当夜，不触发");
        }

        if (SageInteraction.TrySageSlotIndex(machine) is not { } sageSlotIndex)
        {
            return (null, "当夜顺序表上没有贤者格：展示无法排期（过时不候，不顺延到下一夜）");
        }

        if (machine.SlotIndex > sageSlotIndex)
        {
            return (null, "贤者格已经走过：死亡触发的展示当夜不再补开（过时不候，不顺延到下一夜）");
        }

        // 生效判定读**批后账**（与角色维度不同源：角色维度按事件序重建）。真机上该席位的
        // 醉酒 / 中毒变化都由独立批次产出（上报 / 对账），因此批后账 = 死亡时刻状态；
        // 若将来出现"同批死亡之后才改状态"的产出方，这里要按事件序补齐（独立复核 L-3）。
        var effective = Effectiveness(context.State.Seat(death.Seat));
        var note = $"贤者（{death.Seat.Value} 号）被恶魔（{demon.Value} 号，{killerCharacter.Value}）杀死："
            + "记「当晚展示」事实，当夜贤者格开说书人裁定（R-0038）";
        return (
            new SageNight
            {
                Sage = death.Seat,
                Demon = demon,
                DemonCharacter = killerCharacter,
                Effective = effective,
                Note = note,
            },
            null);
    }

    /// <summary>死亡时贤者的能力是否生效：true / false；null = 醉酒 / 中毒维度未观测，判不了（不猜）。</summary>
    private static bool? Effectiveness(SeatStateEntry? entry)
    {
        if (entry is null
            || entry.DrunkValue is not { } drunk
            || entry.PoisonValue is not { } poison)
        {
            return null;
        }

        return drunk == DrunkState.Sober && poison == PoisonState.Healthy;
    }

    /// <summary>本批 / 步骤机里是否已有活跃事实（已开启且未关闭）；死亡批内先开的事实也认。</summary>
    private static bool HasActiveFact(EventTriggerContext context, IReadOnlyList<GameEvent> produced)
    {
        for (var index = produced.Count - 1; index >= 0; index--)
        {
            switch (produced[index])
            {
                case SageNightClosedEvent:
                    return false;
                case SageNightOpenedEvent:
                    return true;
            }
        }

        return context.Machine?.SageNight is not null;
    }

    /// <summary>进入的槽位是不是「贤者触发格」。</summary>
    private static bool IsSageTriggerSlot(EventTriggerContext context, int slotIndex)
    {
        if (context.Machine is not { } machine
            || slotIndex < 0
            || slotIndex >= machine.Plan.Slots.Count)
        {
            return false;
        }

        var slot = machine.Plan.Slots[slotIndex];
        return slot.Kind == StepSlotKind.Trigger && slot.Character == SageAbility.Character;
    }
}
