using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 集骨者的黄昏行动（仅其他夜晚）：每局限一次，选择一名**死亡**玩家，让他重新获得角色能力
/// 直到下个黄昏。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-04 抓取）：
/// 《集骨者》· 角色能力——「每局游戏限一次，在夜晚时*，你可以选择一名死亡的玩家：他重新获得能力
/// 直到下个黄昏。」；· 角色简介——「被选中的玩家仍然保持着死亡状态，但该玩家会重新获得其角色的
/// 能力……如果集骨者死亡，被选择的玩家将不再拥有因集骨者而重新获得的能力。」；
/// · 运作方式——「每个夜晚，唤醒集骨者。集骨者要么摇头表示不使用能力，或者是指向任何一名已死亡的
/// 玩家……被选中的玩家会重新获得其角色能力——用集骨者的『重获能力』提示标记标记该角色。」
/// 「之后集骨者失去其角色能力——将集骨者的『失去能力』提示标记放置到其角色标记旁。下一个黄昏，
/// 被选中的玩家会失去他的能力——移除『重获能力』提示标记。」；
/// · 提示标记——「重获能力」放置条件「如果集骨者未醉酒中毒」、移除时机「在黄昏时，或集骨者死亡或
/// 离场时」；「失去能力」放置条件「只要集骨者选择了已死亡玩家，不论集骨者是否醉酒中毒」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0054：候选 = 本局在局座位里**已死亡**的席位
/// （含旅行者；未观测生死不列）；摇头不算使用；醉酒 / 中毒期间使用照样消耗「每局限一次」的机会
/// （「失去能力」标记由能力使用账本表达）；重获效果落一条
/// <see cref="EffectWindowKind.RegainedAbility"/> 窗口（<c>SourceStateIndependent</c>：标记一经放置
/// 只在下个黄昏 / 集骨者死亡或离场时移除），并在当夜把目标角色尚未进入的**空槽**激活成真实行动格
/// ——说书人据此唤醒他使用刚恢复的能力。
/// </para>
/// </remarks>
internal sealed class BoneCollectorNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => BoneCollectorAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => BoneCollectorAbility.RegainAbility;

    /// <summary>
    /// 用后即失去自身能力（百科提示标记）：本能力的二次结算不可能成立——咖啡师「行动两次」
    /// 不重开本格，也就不产生第二次重获（R-0053 / R-0054 第 5 条）。
    /// </summary>
    public bool SupportsSecondAction => false;

    /// <summary>「每局游戏限一次」：重获 / 咖啡师窗口把总使用次数上限放到 2（R-0052 第 3 条 / R-0054）。</summary>
    public bool IsLimitedPerGame => true;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var dead = context.Seats
            .OrderBy(seat => seat.Value)
            .Where(seat => context.State.Seat(seat)?.LifeValue == LifeState.Dead)
            .ToArray();

        // 「先失去」前置（百科《集骨者》· 规则细节 1）：死亡时**仍保有**能力的玩家不会被再次授予能力
        // ——regain 以"先失去"为前提。受亡骨魔影响的爪牙是首例（R-0056），判定统一读
        // GameState.AbilityPresentOn（R-0054 第 9 条那一份口径）。
        var candidates = dead.Where(seat => !RetainsAbility(context.State, seat)).ToArray();
        var skipped = dead.Length - candidates.Length;

        var options = candidates
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家（已死亡）：重获其角色能力直到下个黄昏",
            })
            .Append(new DecisionOption
            {
                Value = BoneCollectorAbility.Decline,
                Preview = "摇头：本夜不使用能力",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "集骨者（旅行者）：选择一名已死亡的玩家，让他重新获得角色能力直到下个黄昏；"
                + "也可以摇头不用（每局限一次；被选玩家不会得知自己被选中，但可能发现自己又被唤醒）"
                + "（百科《集骨者》· 2026-10-04 抓取 · 角色能力 / 运作方式；R-0054）"
                + SkippedNote(skipped),
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <summary>
    /// 被「先失去」前置挡掉的候选数说明；一个都没有时为空串（不写多余的噪声）。
    /// </summary>
    /// <remarks>
    /// 挡掉这件事**必须说出来**：说书人看不到某个死亡席位出现在候选里，只有这句能告诉他为什么
    /// （不静默跳过，R-0054 第 10 条）。
    /// </remarks>
    private static string SkippedNote(int skipped) => skipped == 0
        ? string.Empty
        : $"。另有 {skipped} 个已死亡的席位仍保有角色能力（死后能力保留，如被亡骨魔杀死的爪牙）："
            + "不会被再次授予——regain 以「先失去」为前提（R-0054 第 10 条）";

    /// <summary>
    /// 该席位是否**仍保有**角色能力：判定不了时按"仍保有"处理——不猜，也不多给一次机会
    /// （与建表期「每局限一次」的保守姿态同向）。
    /// </summary>
    private static bool RetainsAbility(GameState state, SeatId seat) => state.AbilityPresentOn(seat) != false;

    /// <inheritdoc />
    /// <remarks>选择即为结算，不再补一次裁定。</remarks>
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <summary>
    /// 摇头不用不算使用：之后的夜晚仍可再选（百科《集骨者》· 运作方式；与哲学家 R-0036 同款）。
    /// </summary>
    public bool CountsAsUse(AbilityResolutionContext context) =>
        !string.Equals(context.Choice, BoneCollectorAbility.Decline, StringComparison.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.Choice, BoneCollectorAbility.Decline, StringComparison.Ordinal))
        {
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"集骨者的结算缺少合法目标席位：{context.Choice}");
        if (!context.Seats.Contains(target))
        {
            throw new InvalidOperationException($"集骨者选择的目标席位 {target.Value} 不在本局座次名单里");
        }

        var entry = context.State.Seat(target)
            ?? throw new InvalidOperationException(
                $"席位 {target.Value} 不在状态账里：判不了生死，集骨者不能选（不猜，D-0015）");
        if (entry.LifeValue != LifeState.Dead)
        {
            throw new InvalidOperationException(
                $"集骨者只能选择已死亡的玩家：席位 {target.Value} 的生死是 "
                + $"{entry.LifeValue?.ToString() ?? "未观测"}");
        }

        if (RetainsAbility(context.State, target))
        {
            // 「先失去」前置（R-0054 第 10 条）：死亡但从未失去能力的玩家不能被"再次获得"。
            throw new InvalidOperationException(
                $"席位 {target.Value} 死亡时仍保有角色能力（死后能力保留，如被亡骨魔杀死的爪牙）："
                + "集骨者不能让他「再次获得」——regain 以「先失去」为前提"
                + "（百科《集骨者》· 2026-10-04 抓取 · 规则细节 1；rulings.md R-0054 第 10 条 / R-0056）");
        }

        if (entry.CharacterValue is not { } targetCharacter)
        {
            throw new InvalidOperationException(
                $"席位 {target.Value} 的角色尚未观测：算不出要重获的是哪个能力（不猜）");
        }

        if (!context.Outcome.Effective)
        {
            // 中毒 / 醉酒 / 死亡：不放置「重获能力」标记；但选择仍然作出、「失去能力」标记照放
            // ——由本契约的 CountsAsUse=true 把它记进能力使用账本（百科《集骨者》· 提示标记）。
            return [];
        }

        var regainEffect = new PersistentEffect
        {
            // 每局限一次 + 不支持二次结算：标识从槽位稳定键派生，重放时按它认人。
            Id = new EffectId($"{context.SlotKey}:regain:{target.Value}"),
            Source = context.Actor,
            Ability = BoneCollectorAbility.RegainAbility,
            Target = target,
            SourceCharacter = BoneCollectorAbility.Character,
            GrantedCharacter = targetCharacter,
            Window = EffectWindowKind.RegainedAbility,

            // 标记一经放置，只在下个黄昏 / 集骨者死亡或离场时移除：来源此后醉酒 / 中毒
            // 不解除（R-0054 第 2 条）；死亡 / 换角由来源失效链路终止。
            SourceStateIndependent = true,
        };
        var events = new List<GameEvent>
        {
            new PersistentEffectAppliedEvent { Effect = regainEffect },
        };

        // 当夜落格：目标保持死亡，但说书人必须主动唤醒他使用刚恢复的能力（R-0054 第 6 条）。
        // 提示要按「重获窗口已在」的账构建——有些能力的提示自己会查「能力在不在」
        // （女巫：存活人数 / 生死），拿落账前的账建会得到一条空的跳过提示。
        var regained = GameStateMachine.Apply(
            context.State,
            new PersistentEffectAppliedEvent { Effect = regainEffect });
        if (NightSlotActivation.PlanRegained(
                context.Plan,
                context.SlotIndex,
                target,
                targetCharacter,
                regained,
                context.LastDay,
                context.Seats,
                NightActions.Default,
                OverLimitNote(target, targetCharacter, context.State)) is { } activation)
        {
            events.Add(activation);
        }

        return events;
    }

    /// <summary>
    /// 「每局限一次」的总次数已经用满（≥2）时，本夜不重开真实提示而改成一条可归因的跳过
    /// （<see cref="NightSlotActivation.PlanRegained"/> 的 <c>noActionResult</c>）：重获本身照常落账，
    /// 只是该能力已经没有可用的次数（R-0054 第 4 条）。
    /// </summary>
    private static string? OverLimitNote(SeatId target, CharacterId character, GameState state)
    {
        var resolution = NightActions.Resolutions.Find(character);
        if (resolution?.IsLimitedPerGame != true)
        {
            return null;
        }

        var count = state.AbilityUses.UseCount(target, resolution.Ability);
        return count < 2
            ? null
            : $"「{resolution.Ability.Value}」已经用过 {count} 次：重获生效后合计上限仍是 2 次"
                + "（R-0052 第 3 条 / R-0054 第 4 条）";
    }
}
