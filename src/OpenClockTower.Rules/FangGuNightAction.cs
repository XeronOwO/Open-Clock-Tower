using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 方古的夜间行动：除首个夜晚外，每夜选择一名玩家；他死亡。
/// 首次**成功杀死**外来者时改为侵染——外来者变成新的邪恶方古、原方古死亡，整局限一次。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《方古》· 2026-10-01 抓取 · 角色能力——「每个夜晚*，你要选择一名玩家：他死亡。
/// 首次你成功杀死一名外来者时，改为他变成邪恶的方古，且你死亡。这个能力每局游戏仅一次」；
/// · 运作方式 8–14（除首个夜晚外的每个夜晚唤醒方古；首次命中外来者时把「限一次」标记放到魔典中心）；
/// · 提示标记「限一次」（放置条件 = 成功触发能力转化外来者；移除时机 = 持续至整局游戏结束）；
/// · 角色简介 2–6。
/// </para>
/// <para>
/// 四条平台口径：
/// ① 已死亡的目标不会「再次死亡」，因此不构成「成功杀死」——不侵染、原方古也不死
/// （百科《方古》· 2026-10-01 抓取 · 范例：方古攻击已死亡的呆瓜，「他不会再次死亡，所以方古不会死」）；
/// ② 「限一次」是整局事实（<see cref="StepMachineState.FangGuInfection"/>）：即使原方古死亡 / 换角、
/// 之后又出现新的方古，也不再侵染（运作方式 14）；
/// ③ 麻脸巫婆之夜的死亡裁量窗口里，这次攻击记为**携带转化载荷的待定死亡**：说书人「确认」= 按能力侵染，
/// 「阻止」= 击杀与侵染都不发生（<c>docs/standard/rulings.md</c> R-0034）；
/// ④ 侵染写角色 + 阵营两个维度（阵营 = 邪恶，百科明文例外；六维度相互独立）。
/// </para>
/// </remarks>
internal sealed class FangGuNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => FangGuAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => FangGuAbility.KillAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Seats
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "方古选择一名玩家：他死亡；首次成功命中外来者时改为侵染"
                + "（外来者变成新的邪恶方古、原方古死亡，整局限一次；可选自己与已死亡玩家，依《重要细节》三-1）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Outcome.Effective)
        {
            // 中毒 / 醉酒 / 死亡：能力不生效——玩家照常选完目标，但什么都不发生（不提示玩家，三-1）。
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"方古的结算缺少合法目标席位：{context.Choice}");

        var targetState = context.State.Seat(target)
            ?? throw new InvalidOperationException($"方古选择的目标席位 {target.Value} 不在状态账里");

        var life = targetState.LifeValue
            ?? throw new InvalidOperationException($"席位 {target.Value} 的生死尚未观测，击杀不替它猜");
        if (life == LifeState.Dead)
        {
            // 已死亡者不会再次死亡：没有「成功杀死」这回事，不侵染、原方古也不死（TC-fang-gu-02）。
            return [];
        }

        if (IsConvertibleOutsider(context, targetState))
        {
            return ConvertOutcome(context, target);
        }

        // 统一出口：麻脸巫婆之夜（创造了恶魔的那一晚）里，这次击杀记为待定死亡，由说书人裁定（R-0030 第 2 条）。
        return NightKill.Resolve(context, target, Ability, "方古夜间击杀");
    }

    /// <summary>
    /// 这次攻击会不会走侵染：目标当前是外来者，且「限一次」标记还没落下。
    /// </summary>
    private static bool IsConvertibleOutsider(AbilityResolutionContext context, SeatStateEntry targetState)
    {
        if (context.FangGuInfectionConsumed)
        {
            // 标记已经落在魔典中心：新的方古再来杀外来者，外来者正常死亡（运作方式 3 / 14）。
            return false;
        }

        var character = targetState.CharacterValue
            ?? throw new InvalidOperationException(
                $"席位 {targetState.Seat.Value} 的角色尚未观测，判不了这次攻击是否为外来者");

        return SectsAndVioletsRoster.TypeOf(character) == CharacterType.Outsider;
    }

    /// <summary>
    /// 侵染的两种出口：麻脸巫婆之夜窗口里记**携带转化载荷的待定死亡**；否则直接落转化事实。
    /// </summary>
    private static IReadOnlyList<GameEvent> ConvertOutcome(AbilityResolutionContext context, SeatId target)
    {
        if (context.PitHagNightActive)
        {
            return
            [
                new DeferredDeathRecordedEvent
                {
                    Target = target,
                    Source = context.Actor,
                    Ability = FangGuAbility.KillAbility,
                    Note = "方古夜间击杀（首次命中外来者：说书人确认则按侵染结算——外来者变成新的邪恶方古、"
                        + "原方古死亡；阻止则两者都不发生。平台口径见 rulings.md R-0034）",
                    Transformation = new DeferredTransformation
                    {
                        Target = target,
                        Character = FangGuAbility.Character,
                        Alignment = Alignment.Evil,
                        Dies = context.Actor,
                        Note = $"外来者（{target.Value} 号）变成新的邪恶方古，原方古（{context.Actor.Value} 号）死亡",
                    },
                },
            ];
        }

        var effectId = new EffectId($"{context.SlotKey}:fang-gu-infect");
        return
        [
            new InstantaneousEffectAppliedEvent
            {
                Effect = new InstantaneousEffect
                {
                    Id = effectId,
                    Source = context.Actor,
                    Ability = FangGuAbility.KillAbility,
                    Target = target,
                },
            },
            new SeatStateChangedEvent
            {
                // 被攻击的外来者**不死亡**：角色与阵营同批变化（百科《方古》· 角色简介 2 / 运作方式 13）。
                Seat = target,
                Character = FangGuAbility.Character,
                Alignment = Alignment.Evil,
                Reason = FangGuAbility.InfectionReason(context.Actor, target),
                CausedBy = context.Actor,
                EffectId = effectId,
            },
            new SeatStateChangedEvent
            {
                // 原方古死亡：同一次侵染的另一半事实（运作方式 11 / 13）。
                Seat = context.Actor,
                Life = LifeState.Dead,
                Reason = FangGuAbility.SourceDeathReason(context.Actor),
                CausedBy = context.Actor,
                EffectId = effectId,
            },
            new FangGuInfectionRecordedEvent
            {
                Seat = target,
                Source = context.Actor,
            },
        ];
    }
}
