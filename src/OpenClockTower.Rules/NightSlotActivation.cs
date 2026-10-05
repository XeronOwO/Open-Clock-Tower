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
    /// 需要绑定时返回事件（激活既有格，或按 R-0055 **追加**一格）；否则返回 null——三种情况都不是静默：
    /// 不在夜晚顺序表上（本来就没有夜间行动）、时机已过（那一格已经进入过，过时不候）、
    /// 在表上却没有契约（进入那一格时由步骤机显式阻塞）；行动槽位本来绑的就是这位行动者时也返回 null。
    /// </returns>
    public static GameEvent? Plan(
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

        // 本阶段根本没有它的行动格（"首个夜晚"能力用在其他夜晚）：追加一格（R-0055）。
        return NightSlotInsertion.Plan(
            plan,
            slotIndex,
            actor,
            character,
            actorCharacter: character,
            requiredLife: LifeState.Alive,
            state,
            lastDay,
            seats,
            catalog);
    }

    /// <summary>
    /// 哲学家「获得能力」的**当夜**落格：被获得角色的格还没进入、且那一格没有行动者时，
    /// 把那一格交给获得者代行；本阶段没有它的格时按 R-0055 **追加**一格。
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
    public static GameEvent? PlanGranted(
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

        // 本阶段根本没有它的行动格（"首个夜晚"能力用在其他夜晚）：追加一格（R-0055）。
        return NightSlotInsertion.Plan(
            plan,
            slotIndex,
            actor,
            grantedCharacter,
            state.Seat(actor)?.CharacterValue
                ?? throw new InvalidOperationException(
                    $"席位 {actor.Value} 的角色尚未观测：算不出追加格的依赖（不猜，D-0015）"),
            requiredLife: LifeState.Alive,
            state,
            lastDay,
            seats,
            catalog);
    }

    /// <summary>
    /// 集骨者「重获能力」的当夜落格：被选中的**死亡**玩家在自己角色的格还没进入、且那一格没有
    /// 存活持有者时，把这一格绑成他的真实行动格——说书人据此唤醒他使用刚恢复的角色能力（R-0054）；
    /// 本阶段没有它的格（「首个夜晚」能力用在其他夜晚）时按 R-0055 **追加**一格。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="Plan"/> 的差别：行动者**保持死亡**，因此依赖不写 <c>RequiredLife</c>；
    /// 入槽放行由 <see cref="StepSlotEntry.UnavailableReason"/> 按「重获窗口仍在」判定。
    /// 窗口在这之后终止（集骨者死亡 / 下个黄昏）时，这一格进入即被显式跳过。
    /// </para>
    /// <para>
    /// 返回 null 的三种情形都不是静默：这一格已经进入过（过时不候）、那一格已有存活持有者
    /// （角色能力归活着的那位）、契约未实现（入格时按空槽处理）。「首夜能力用在其他夜晚」
    /// 自 R-0055 起改为**追加一格**（依据见 <see cref="NightSlotInsertion"/>），不再是不造格。
    /// </para>
    /// <para>
    /// <paramref name="noActionResult"/> 非空时把这一格绑成「本夜无行动」（空选项 + Skip）：
    /// 重获生效但「每局限一次」的总次数已经用满（≥2）——照样入格，由入格路径记一条可归因的
    /// 跳过，而不是不声不响地不唤醒（R-0054 第 4 条）。
    /// </para>
    /// </remarks>
    public static GameEvent? PlanRegained(
        StepPlan? plan,
        int slotIndex,
        SeatId actor,
        CharacterId character,
        GameState state,
        DayRecord? lastDay,
        IReadOnlyList<SeatId> seats,
        INightActionCatalog catalog,
        string? noActionResult = null)
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
            if (slot.Character != character)
            {
                continue;
            }

            if (slot.Kind != StepSlotKind.Empty)
            {
                // 那一格已经有行动者（角色仍有存活持有者，或已被别的能力激活）：归它自己，不动。
                return null;
            }

            if (catalog.Find(character) is not { } action)
            {
                // 契约未实现：不在这里造提示，这一格照旧空转（进入时按空槽位处理）。
                return null;
            }

            return new SlotActivatedEvent
            {
                SlotIndex = index,
                SlotId = slot.Id,
                Actor = actor,
                Prompt = noActionResult is null
                    ? action.BuildPrompt(new NightActionContext
                    {
                        Actor = actor,
                        Seats = seats,
                        State = state,
                        LastDay = lastDay,
                    })
                    : new ChoicePrompt
                    {
                        Context = noActionResult,
                        Options = [],
                        OnNoOption = NoOptionBehavior.Skip,
                    },
                Dependencies =
                [
                    new SeatDependency
                    {
                        // 行动者保持死亡：生死一维不约束；角色换了这一格就失去意义。
                        Seat = actor,
                        RequiredLife = null,
                        RequiredCharacter = character,
                    },
                ],
            };
        }

        // 本阶段根本没有它的行动格（"首个夜晚"能力用在其他夜晚）：追加一格（R-0055）。
        return NightSlotInsertion.Plan(
            plan,
            slotIndex,
            actor,
            character,
            actorCharacter: character,
            requiredLife: null,
            state,
            lastDay,
            seats,
            catalog);
    }

    /// <summary>
    /// 「本夜无行动」格的**重开**：计划期把已经用满「每局限一次」的角色判成无行动
    /// （行动槽位但提示没有合法选项），更早的结算（咖啡师「行动两次」）让它重新可用时，
    /// 把这一格换成真实提示。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="Plan"/> 的差别：只认**同一行动者**且**当前提示没有合法选项**的未进入格——
    /// 换手重绑（行动者不同）与空槽激活仍由 <see cref="Plan"/> 负责；已经开着选项的格再绑就是重复。
    /// 依据：咖啡师效果 2「已用过的『每局游戏限一次』可以再用」（百科《咖啡师》· 2026-10-04 抓取 ·
    /// 角色简介），平台口径见 <c>docs/standard/rulings.md</c> R-0052 第 3 条。
    /// </para>
    /// <para>
    /// 返回 null 的三种情形都不是静默：这一格已经进入过（过时不候）、那一格本来就是行动格
    /// （窗口的第二次结算由槽位重入负责，不需要重开）、契约未实现（进入时按原提示跳过）。
    /// </para>
    /// </remarks>
    internal static SlotActivatedEvent? PlanUpgrade(
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

        for (var index = slotIndex + 1; index < plan.Slots.Count; index++)
        {
            var slot = plan.Slots[index];
            if (slot.Character != character)
            {
                continue;
            }

            if (slot.Kind != StepSlotKind.Action
                || slot.Actor != actor
                || slot.Prompt is not { } prompt
                || prompt.HasOptions)
            {
                return null;
            }

            if (catalog.Find(character) is not { } action)
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
}
