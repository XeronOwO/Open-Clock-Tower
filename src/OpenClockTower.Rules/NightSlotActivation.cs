using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 「这个角色今夜刚被创造出来 / 换了持有者 → 这一格待会儿要唤醒他」的共享实现。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 麻脸巫婆条——「一旦说书人决定杀死某个还未唤醒的恶魔，
/// 这名恶魔玩家当晚就不会被唤醒。否则，就需要唤醒这名玩家」「例如玩家变成了恶魔，
/// 则在恶魔行动时一并唤醒，通知角色变化并让他执行相应行动」；平台口径见
/// <c>docs/standard/rulings.md</c> R-0030 第 6 条。
/// </para>
/// <para>
/// 角色**换手**（舞蛇人交换角色等）时同样调用本方法：把尚未进入的行动槽位重新绑定给新持有者，
/// 提示按新持有者重建——依据《重要细节》· 2026-10-01 抓取 · 七（换了角色 = 获得新角色的能力），
/// 平台口径见 R-0032。
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
    /// 为「席位 <paramref name="actor"/> 此刻持有 <paramref name="character"/>」算一次槽位绑定：
    /// 空槽位 → 激活；行动槽位且行动者不是它 → 换手重绑。
    /// </summary>
    /// <returns>
    /// 需要绑定时返回事件；否则返回 null——三种情况都不是静默：
    /// 不在夜晚顺序表上（本来就没有夜间行动）、时机已过（那一格已经进入过，过时不候）、
    /// 在表上却没有契约（进入那一格时由步骤机显式阻塞）；行动槽位本来绑的就是这位行动者时也返回 null。
    /// </returns>
    public static SlotActivatedEvent? Plan(
        StepPlan? plan,
        int slotIndex,
        SeatId actor,
        CharacterId character,
        GameState state,
        DayRecord? lastDay,
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

            if (slot.Kind == StepSlotKind.Trigger)
            {
                // 触发格不是行动格：它是否开交互请求由触发管线按步骤机事实决定（理发师格），
                // 不因「有人获得了这个角色」而激活 / 重绑——他在场（或刚被换入）也不行动。
                continue;
            }

            if (catalog.Find(character) is not { } action)
            {
                // 在表上却没有契约：不在这里造提示，进入那一格时由步骤机显式阻塞。
                return null;
            }

            if (slot.Kind == StepSlotKind.Action && slot.Actor == actor)
            {
                // 这一格本来就绑给了此刻的持有者：无需绑定（重复事件由折叠层显式拒绝）。
                return null;
            }

            if (slot.Kind is not (StepSlotKind.Action or StepSlotKind.Empty))
            {
                // 不是角色槽位（节拍 / 黎明等）：数据缺陷，交给折叠层拒绝。
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
                    LastDay = lastDay,
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

    /// <summary>
    /// 哲学家「获得能力」的**当夜**落格：被获得角色的格还没进入、且那一格没有行动者时，
    /// 把那一格交给获得者代行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="Plan"/> 的两点差别都来自「行动者不是那一格角色的持有者」：
    /// ① **只碰空槽位**——那一格已经有行动者（被获得角色在场）时不动它：持有者照常被唤醒
    /// （醉酒 → 能力不生效），不产生"醉酒者没被唤醒"这种可观测信息；
    /// ② 依赖写获得者**本人**的角色（他的这份能力来自他自己的角色，换了角色即失去）。
    /// </para>
    /// <para>
    /// 口径见 <c>docs/standard/rulings.md</c> R-0036。返回 null 的三种情形都不是静默：
    /// 那一格已经进入过（过时不候）、那一格有行动者（归它自己）、那一格没有契约（进入时按空槽位处理）。
    /// </para>
    /// </remarks>
    public static SlotActivatedEvent? PlanGranted(
        StepPlan? plan,
        int slotIndex,
        SeatId actor,
        CharacterId grantedCharacter,
        GameState state,
        DayRecord? lastDay,
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

        for (var index = slotIndex + 1; index < plan.Slots.Count; index++)
        {
            var slot = plan.Slots[index];
            if (slot.Character != grantedCharacter)
            {
                continue;
            }

            if (slot.Kind != StepSlotKind.Empty)
            {
                // 那一格已经有人（被获得角色在场，或已被别的能力激活）：归它自己，不动。
                return null;
            }

            if (catalog.Find(grantedCharacter) is not { } action)
            {
                // 契约未实现：不在这里造提示，这一格照旧空转（进入时按空槽位处理）。
                return null;
            }

            var actorCharacter = state.Seat(actor)?.CharacterValue
                ?? throw new InvalidOperationException(
                    $"席位 {actor.Value} 的角色尚未观测：算不出代行槽位的依赖（不猜，D-0015）");
            if (actorCharacter == grantedCharacter)
            {
                // 自己获得自己的角色：不是代行，交给常规激活口径。
                return null;
            }

            return new SlotActivatedEvent
            {
                SlotIndex = index,
                SlotId = slot.Id,
                Actor = actor,
                ActorCharacter = actorCharacter,
                Prompt = action.BuildPrompt(new NightActionContext
                {
                    Actor = actor,
                    Seats = seats,
                    State = state,
                    LastDay = lastDay,
                }),
                Dependencies =
                [
                    new SeatDependency
                    {
                        Seat = actor,
                        RequiredLife = LifeState.Alive,
                        RequiredCharacter = actorCharacter,
                    },
                ],
            };
        }

        return null;
    }
}
