using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 诺-达鲺的夜间行动：每个夜晚*（首夜不行动）选择一名玩家，他死亡。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《诺-达鲺》· 2026-10-01 抓取 · 角色能力 / 运作方式：
/// 「每个夜晚*，你要选择一名玩家：他死亡」；「除首个夜晚外的每个夜晚，唤醒诺-达鲺……该玩家死亡」。
/// </para>
/// <para>
/// 「选择一名玩家」包含自己与已死亡玩家：百科《重要细节》· 2026-10-01 抓取 · 三-1——
/// 「在夜晚选择『任意玩家』，意思就是可以选择自己或是选择已死亡的玩家」；
/// 能力原文没有写「不能」选谁，因此合法集合 = 全体席位。死亡提示标记只落在**当前存活**的玩家身上
/// （同页 提示标记·死亡 的放置条件），已死亡者无法再次死亡（术语《生死》）。
/// </para>
/// </remarks>
internal sealed class NoDashiiNightAction : INightAction, IAbilityResolution
{
    /// <summary>夜间击杀的能力标识（与常驻中毒分开）。</summary>
    public static readonly AbilityId KillAbility = new("no-dashii");

    private static readonly CharacterId NoDashii = new("no-dashii");

    /// <inheritdoc />
    public CharacterId Character => NoDashii;

    /// <inheritdoc />
    public AbilityId Ability => KillAbility;

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
            Context = "诺-达鲺选择一名玩家：他死亡（可选自己或已死亡玩家，依《重要细节》三-1）",
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
            // 中毒 / 醉酒 / 死亡：能力不生效——玩家照常选完目标，但什么都不发生；
            // 也不通知玩家能力失败（百科《重要细节》三-1：让他们以清醒和健康的状态执行行动）。
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"诺-达鲺的结算缺少合法目标席位：{context.Choice}");

        var life = context.State.Seat(target)?.LifeValue
            ?? throw new InvalidOperationException($"席位 {target.Value} 的生死尚未观测，击杀不替它猜");

        if (life == LifeState.Dead)
        {
            // 已死亡的玩家无法再次死亡（术语《生死》）：能力用过，但不再产生死亡事实。
            return [];
        }

        var effectId = new EffectId($"{context.PlanLabel}:{context.SlotId}:kill");
        return
        [
            new InstantaneousEffectAppliedEvent
            {
                Effect = new InstantaneousEffect
                {
                    Id = effectId,
                    Source = context.Actor,
                    Ability = Ability,
                    Target = target,
                },
            },
            new SeatStateChangedEvent
            {
                Seat = target,
                Life = LifeState.Dead,
                Reason = "诺-达鲺夜间击杀",
                CausedBy = context.Actor,
                EffectId = effectId,
            },
        ];
    }
}
