using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 女巫的夜间行动：每个夜晚选择一名玩家诅咒——他若在下个白天发起提名就会死亡，提名仍然生效。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《女巫》· 2026-10-01 抓取 · 角色能力 / 角色简介 1 / 运作方式：
/// 「每个夜晚，你要选择一名玩家：如果他明天白天发起提名，他死亡」；
/// 「那名玩家如果在下个白天提名了任何玩家，就会死亡。尽管如此，他的提名仍然生效」。
/// </para>
/// <para>
/// 目标集合 = 全体席位：能力原文没有写「不能」选谁，且百科《重要细节》· 2026-10-01 抓取 · 三-1
/// 规定「在夜晚选择『任意玩家』，意思就是可以选择自己或是选择已死亡的玩家」——提示与技巧里也
/// 明说可以诅咒已死亡的玩家（用来隐藏自己的能力）。
/// </para>
/// <para>
/// 诅咒是 <see cref="PersistentEffect"/> 且 <see cref="PersistentEffect.Dimension"/> 为 null：
/// 它不压制任何玩家维度，只在"下个白天发起提名"这一刻产生后果（触发见 <see cref="WitchCurseTrigger"/>）。
/// 来源醉酒 / 中毒时**挂起**、恢复后继续生效（<c>docs/standard/rulings.md</c> R-0012）；
/// 来源死亡 / 换角色时由折叠链路终止。
/// </para>
/// </remarks>
internal sealed class WitchNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => WitchAbility.Witch;

    /// <inheritdoc />
    public AbilityId Ability => WitchAbility.ActionAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (WitchAbility.InForce(context.State, context.Seats) == false)
        {
            // 只剩三名存活玩家：能力已失去，本夜不再把女巫叫起来。
            // 空选项 + Skip（R-0009）：槽位照走配额、不发请求，跳过原因进事件流供事后审计。
            return new ChoicePrompt
            {
                Context = "只剩下三名存活玩家：女巫已经失去这个能力（百科《女巫》· 2026-10-01 抓取 · 角色简介），"
                    + "本夜不再行动",
                Options = [],
                OnNoOption = NoOptionBehavior.Skip,
            };
        }

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
            Context = "女巫选择一名玩家诅咒：他若在下个白天发起提名就会死亡，提名仍然生效"
                + "（可选自己与已死亡玩家，依《重要细节》三-1）",
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
            // 中毒 / 醉酒 / 死亡：能力不生效——按提示标记的放置条件，此时**不放置**「被诅咒」
            // （百科《女巫》· 2026-10-01 抓取 · 提示标记「若此时女巫醉酒中毒，不放置该标记」）。
            return [];
        }

        if (WitchAbility.InForce(context.State, context.Seats) == false)
        {
            // 计划建好之后、结算之前存活人数掉到三名（同一夜更早的死亡）：能力已失去，
            // 不落任何效果——账上不留假事实。
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"女巫的结算缺少合法目标席位：{context.Choice}");

        return
        [
            new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = WitchAbility.CurseEffectId(context.PlanLabel, context.SlotId),
                    Source = context.Actor,
                    Ability = WitchAbility.CurseAbility,
                    Target = target,
                    SourceCharacter = WitchAbility.Witch,

                    // Dimension 保持 null：诅咒不压制任何玩家维度，只在白天提名那一刻产生后果。
                },
            },
        ];
    }
}
