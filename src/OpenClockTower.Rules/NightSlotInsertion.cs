using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 非首个夜晚获得的「首个夜晚」能力的**追加格**：这类能力的行动格只出现在首夜顺序表上，
/// 其他夜晚表里根本没有它的位置，因此不能靠「激活已有槽位」落地，只能**追加**一格。
/// </summary>
/// <remarks>
/// <para>
/// 依据（均为百科 · 2026-10-01 抓取）：
/// 《重要细节》· 七——「如果新的能力原本**只在游戏的首个夜晚**产生效果，那么它会在**角色变化的当晚**
/// 产生效果」「如果带有『在你的首个夜晚』角色在游戏中途被创造，这些角色会**尽可能快**地进行
/// 进场能力的效果结算，因为他们的能力不会在夜晚顺序表的『其他夜晚』部分出现。但仍需注意，
/// **绝大部分获取信息的能力应该在所有会造成死亡的效果之后结算**」；
/// 《获得能力》· 能力简介——非首个夜晚获得进场能力时「在夜晚顺序表中……**插入结算**这些效果，
/// 唤醒该玩家并触发相应的能力」；时机晚于该点时「**立即生效并进行结算**」；
/// 《哲学家》· 运作方式 8 / 范例 2——「如果这个角色能力是『首个夜晚』能力，他会在**当晚**使用该能力」
/// 「在第三个夜晚，哲学家选择获得钟表匠的能力。**当晚**，他得知了恶魔与爪牙之间的最近的距离」；
/// 《集骨者》· 角色简介 1——「『在你的首个夜晚』或『每局游戏限一次』的能力……可以在**黄昏之前
/// 再次使用**这些能力，即使先前已经使用过了」。
/// </para>
/// <para>
/// 平台口径（追加位、多名持有者的顺序、不追加的情形）见 <c>docs/standard/rulings.md</c> R-0055。
/// 三条来源——哲学家「获得能力」、集骨者「重获能力」、角色变更（麻脸巫婆 / 理发师 / 舞蛇人 /
/// 说书人手工上报）——都接到这里，避免三套口径。
/// </para>
/// </remarks>
internal static class NightSlotInsertion
{
    /// <summary>
    /// 算一格「获得的进场能力」追加位；不适用时返回 null（不是静默：调用方另有各自的
    /// 激活路径、跳过记录或空槽位口径）。
    /// </summary>
    /// <param name="plan">本夜计划；缺失（阶段外结算 / 内核夹具）时返回 null。</param>
    /// <param name="slotIndex">获得发生的那一格（追加位不得早于它的后一格）。</param>
    /// <param name="actor">持有这份能力的席位。</param>
    /// <param name="grantedCharacter">被获得的角色（结算契约的检索键）。</param>
    /// <param name="actorCharacter">行动者本人的角色；与 <paramref name="grantedCharacter"/> 不同时构造代行格。</param>
    /// <param name="requiredLife">依赖里声明的生死要求；重获路径传 null（持有者保持死亡）。</param>
    /// <param name="state">按「获得已落账」的账传进来（提示要能读到这份能力）。</param>
    internal static SlotInsertedEvent? Plan(
        StepPlan? plan,
        int slotIndex,
        SeatId actor,
        CharacterId grantedCharacter,
        CharacterId actorCharacter,
        LifeState? requiredLife,
        GameState state,
        DayRecord? lastDay,
        IReadOnlyList<SeatId> seats,
        INightActionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(catalog);

        // 只有「其他夜晚」才谈得上"追加"：首夜这一格本来就在计划里，走既有激活 / 代行路径。
        if (plan is null || plan.Phase != GamePhase.OtherNight)
        {
            return null;
        }

        // 只在"这类能力在其他夜晚表上没有位置"时追加（《重要细节》七的原话）。
        if (!IsFirstNightOnlyAction(grantedCharacter, VariantOf(plan)))
        {
            return null;
        }

        // 还没轮到 / 已经没有位置可落：当前格之后连一格都不剩（当前格是最后一格，或整夜已走完）
        // 就不追加——过时不候（《钟楼谜团隐性规则汇总》§6 同族），也避免产出越界的追加事件。
        if (slotIndex + 1 >= plan.Slots.Count)
        {
            return null;
        }

        if (catalog.Find(grantedCharacter) is not { } action)
        {
            // 契约未实现：不在这里造提示（建表期已按 plan.contract_missing 拒绝，这里只是防御）。
            return null;
        }

        var id = EntrySlotId(actor, grantedCharacter);
        if (plan.Slots.Any(slot => slot.Id == id))
        {
            // 同一席位这一夜已经追加过**同一项能力**：二次获得 = 替换（R-0053），沿用那一格，不再追加。
            // 标识里带席位：不同持有者各自有自己的格（原文把多名持有者的唤醒顺序交给说书人，
            // 平台按追加发生的倒序依次唤醒，见 R-0055 第 3 条）。
            return null;
        }

        var dependencies = new List<SeatDependency>
        {
            new()
            {
                Seat = actor,

                // 重获路径的持有者保持死亡：生死一维不约束（与 PlanRegained 同款）；
                // 代行 / 角色变更路径锁存活。
                RequiredLife = requiredLife,
                RequiredCharacter = actorCharacter,
            },
        };

        var prompt = action.BuildPrompt(new NightActionContext
        {
            Actor = actor,
            Seats = seats,
            State = state,
            LastDay = lastDay,
        });

        return new SlotInsertedEvent
        {
            Index = InsertionIndex(plan, slotIndex),
            Slot = actorCharacter == grantedCharacter
                ? StepSlot.Action(id, actor, prompt, dependencies, grantedCharacter)
                : StepSlot.GrantedAction(id, actor, actorCharacter, prompt, dependencies, grantedCharacter),
        };
    }

    /// <summary>
    /// 追加位：**本夜最后一条可能致死的行动格之后**；获得发生得更晚时紧随当前格之后
    /// （「立即生效并进行结算」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上界是**黎明等待格**：追加格必须在夜内结算，越过它就成了"夜里的事发生在黎明之后"。
    /// 首版顺序表里恶魔段之后还有信息行动格，所以正常情况下用不到这条上限；它挡的是
    /// "最后一个角色行动恰好是恶魔"这类计划形状（含测试夹具与将来改表）。
    /// </para>
    /// <para>
    /// 同一夜多次追加时，后追加的落在先追加的**之前**（每格都"紧跟致死段之后"的自然结果）——
    /// 规则原文把多名持有者的唤醒顺序交给说书人，平台取这个确定性口径（R-0055 第 3 条）。
    /// </para>
    /// </remarks>
    private static int InsertionIndex(StepPlan plan, int slotIndex)
    {
        var afterDeaths = NightDeathWindow.LastCapableSlotIndex(plan) is { } last
            ? Math.Max(last + 1, slotIndex + 1)
            : slotIndex + 1;

        if (DawnSlotIndex(plan) is not { } dawn)
        {
            return afterDeaths;
        }

        // 封顶到黎明等待格之前；同时保住"追加位必须晚于当前格"这条不变量（计划形状异常时它优先）。
        return Math.Max(Math.Min(afterDeaths, dawn), slotIndex + 1);
    }

    /// <summary>黎明等待格的下标；没有（不该发生）时返回 null。</summary>
    private static int? DawnSlotIndex(StepPlan plan)
    {
        for (var index = 0; index < plan.Slots.Count; index++)
        {
            if (plan.Slots[index].Kind == StepSlotKind.DawnWait)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// 追加格的稳定标识：<c>{被获得角色}@{行动者席位}</c>——语义身份就是"这一席今夜可以结算这项
    /// 进场能力"，同一席位同一项能力只追加一格（二次获得 = 替换，R-0053）；不同席位各自一格。
    /// 重放按它认人（与效果 / 请求标识同一姿态）。
    /// </summary>
    private static StepSlotId EntrySlotId(SeatId actor, CharacterId grantedCharacter) =>
        new($"{grantedCharacter.Value}@{actor.Value}");

    /// <summary>该角色是不是「首个夜晚」才行动：首夜表上有角色行动格、其他夜晚表上没有。</summary>
    private static bool IsFirstNightOnlyAction(CharacterId character, NightOrderVariant variant) =>
        NightOrderTable.HasAction(character, GamePhase.FirstNight, variant)
        && !NightOrderTable.HasAction(character, GamePhase.OtherNight, variant);

    /// <summary>
    /// 计划里记着的建表口径；没注明（测试夹具 / 占位数据）时按 <see cref="NightOrderVariant.Original"/> 判——
    /// 首版《梦殒春宵》两套口径的**成员集合一致**（只差顺序，R-0014 的差异清单里没有成员差异），
    /// 因此这个兜底不改变本脚本下的判定结果。
    /// </summary>
    private static NightOrderVariant VariantOf(StepPlan plan) =>
        Enum.TryParse<NightOrderVariant>(plan.Variant, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : NightOrderVariant.Original;
}
