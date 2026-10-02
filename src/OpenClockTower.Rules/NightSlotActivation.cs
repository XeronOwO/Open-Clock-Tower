using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 「这个角色今夜刚被创造出来 → 这一格待会儿要唤醒他」的共享实现。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 麻脸巫婆条——「一旦说书人决定杀死某个还未唤醒的恶魔，
/// 这名恶魔玩家当晚就不会被唤醒。否则，就需要唤醒这名玩家」「例如玩家变成了恶魔，
/// 则在恶魔行动时一并唤醒，通知角色变化并让他执行相应行动」；平台口径见
/// <c>docs/standard/rulings.md</c> R-0030 第 6 条。
/// </para>
/// <para>
/// 由**角色契约在结算时**调用，不放在提交管线的批末：结算发生在槽位推进之前，
/// 因此紧邻其后的那一格（原本口径下「麻脸巫婆 → 方古」就是相邻的）也能被正确激活；
/// 批末补全会漏掉已经被进入的那一格。
/// </para>
/// </remarks>
public static class NightSlotActivation
{
    /// <summary>
    /// 为「席位 <paramref name="actor"/> 刚变成 <paramref name="character"/>」算一次激活。
    /// </summary>
    /// <returns>
    /// 需要激活时返回事件；否则返回 null——三种情况都不是静默：
    /// 不在夜晚顺序表上（本来就没有夜间行动）、时机已过（那一格已经进入过，过时不候）、
    /// 在表上却没有契约（进入那一格时由步骤机显式阻塞）。
    /// </returns>
    public static SlotActivatedEvent? Plan(
        StepPlan? plan,
        int slotIndex,
        SeatId actor,
        CharacterId character,
        GameState state,
        IReadOnlyList<SeatId> seats,
        INightActionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(catalog);

        if (plan is null)
        {
            return null;
        }

        // 只往后看：之前的下标都已经走过（《钟楼谜团隐性规则汇总》§6「过时不候」）。
        for (var index = slotIndex + 1; index < plan.Slots.Count; index++)
        {
            var slot = plan.Slots[index];
            if (slot.Character != character)
            {
                continue;
            }

            if (slot.Kind == StepSlotKind.Action || catalog.Find(character) is not { } action)
            {
                return null;
            }

            return new SlotActivatedEvent
            {
                SlotIndex = index,
                SlotId = slot.Id,
                Actor = actor,
                Prompt = action.BuildPrompt(new NightActionContext
                {
                    Actor = actor,
                    Seats = seats,
                    State = state,
                }),
                Dependencies =
                [
                    new SeatDependency
                    {
                        Seat = actor,
                        RequiredLife = LifeState.Alive,
                        RequiredCharacter = character,
                    },
                ],
            };
        }

        return null;
    }
}
