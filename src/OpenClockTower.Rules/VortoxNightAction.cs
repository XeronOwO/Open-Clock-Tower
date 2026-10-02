using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 涡流的夜间行动：除首个夜晚外的每个夜晚选择一名玩家，他死亡（击杀），并提供「镇民信息必假」的标注。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《涡流》· 2026-10-01 抓取 · 角色能力 / 运作方式——「每个夜晚*，你要选择一名玩家：他死亡」
/// 「除首个夜晚外的每个夜晚，唤醒涡流……将该玩家杀死」。
/// </para>
/// <para>
/// 「选择一名玩家」包含自己与已死亡玩家（同诺-达鲺的实现口径，百科《重要细节》三-1）；
/// 已死亡者无法再次死亡，因此不重复产出死亡事实。
/// 涡流的信息干扰见 <see cref="VortoxInterference"/> 与 <c>docs/standard/rulings.md</c> R-0028：
/// 平台只标注"必须为假"与归因，信息内容仍由说书人裁定（D-0002）。
/// </para>
/// </remarks>
internal sealed class VortoxNightAction : INightAction, IAbilityResolution
{
    /// <summary>夜间击杀的能力标识。</summary>
    public static readonly AbilityId KillAbility = new("vortox");

    private static readonly CharacterId Vortox = new("vortox");

    /// <inheritdoc />
    public CharacterId Character => Vortox;

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
            Context = "涡流选择一名玩家：他死亡（可选自己或已死亡玩家，依《重要细节》三-1）",
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
            ?? throw new InvalidOperationException($"涡流的结算缺少合法目标席位：{context.Choice}");

        var life = context.State.Seat(target)?.LifeValue
            ?? throw new InvalidOperationException($"席位 {target.Value} 的生死尚未观测，击杀不替它猜");

        if (life == LifeState.Dead)
        {
            return [];
        }

        // 统一出口：麻脸巫婆之夜（创造了恶魔的那一晚）里，这次击杀记为待定死亡，由说书人裁定（R-0030 第 2 条）。
        return NightKill.Resolve(context, target, Ability, "涡流夜间击杀");
    }
}
