using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 哲学家的夜间行动：每局限一次，选择获得一名镇民 / 外来者角色的能力（不变身）；也可以摇头不用。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《哲学家》· 2026-10-01 抓取 · 角色能力——「每个夜晚，你要选择一名玩家：他死亡」不适用，
/// 本角色的能力是「每局游戏限一次，你要选择获得一名镇民或外来者角色的能力」；· 角色简介 1–2
/// （「他获得那个角色的能力，但不会变成那个角色」）；· 运作方式 4 / 11–13（在场角色的持有者醉酒、
/// 每夜唤醒他、摇头或指向一个镇民 / 外来者图标）。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0036：
/// ① 「获得能力」落成账上一条常驻标记效果（带被获得的角色），**不写角色维度**（不变身）；
/// ② 常驻醉酒由 <see cref="PhilosopherDrunkSource"/> 动态对账（被选角色进场 / 换持有者时移动）；
/// ③ 获得的能力在**被获得角色的格**上执行（那一格没有行动者时，当夜就地激活）；那一格有持有者时，
/// 改在**他自己的格**上代行——他的格在那种情况下不再是"选择"格；
/// ④ 二次获得（咖啡师「行动两次」/ 集骨者「重获能力」）= **替换**旧授予，见 R-0053（Decided）。
/// </para>
/// </remarks>
internal sealed class PhilosopherNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => PhilosopherAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => PhilosopherAbility.GrantAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = PhilosopherAbility.SelectableCharacters()
            .Select(character => new DecisionOption
            {
                Value = character.Value,
                Preview = PhilosopherAbility.DisplayNameOf(character),
            })
            .Append(new DecisionOption
            {
                Value = PhilosopherAbility.Decline,
                Preview = "摇头：本夜不使用能力",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "哲学家选择获得一名镇民 / 外来者角色的能力（每局限一次，不变身），或摇头不用；"
                + "该角色在场时它的持有者醉酒（百科《哲学家》· 2026-10-01 抓取 · 角色能力 / 运作方式）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <summary>
    /// 摇头不用不算使用：之后的夜晚仍可再选（「每局限一次」约束的是「获得」，R-0036 / R-0040）。
    /// </summary>
    public bool CountsAsUse(AbilityResolutionContext context) =>
        !string.Equals(context.Choice, PhilosopherAbility.Decline, StringComparison.Ordinal);

    /// <summary>
    /// 「获得能力」支持咖啡师「行动两次」的二次结算：第二次获得按**替换**语义收口——先终止旧授予，
    /// 再落新授予；「每局限一次」的总次数上限为 2（<c>docs/standard/rulings.md</c> R-0053 Decided）。
    /// </summary>
    public bool SupportsSecondAction => true;

    /// <summary>「每局游戏限一次」：总使用次数上限 2（咖啡师窗口内可再选一次，R-0052 第 3 条）。</summary>
    public bool IsLimitedPerGame => true;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Outcome.Effective)
        {
            // 中毒 / 醉酒 / 死亡：能力不生效——照常选完，但什么都不发生。这次使用仍进能力使用账本
            // （「限次能力醉酒期间使用即浪费」，百科《重要细节》三-3），建表据此不再给他第二次选择。
            return [];
        }

        if (string.Equals(context.Choice, PhilosopherAbility.Decline, StringComparison.Ordinal))
        {
            // 摇头：没有获得任何能力，之后的夜里还可以再选（「每局限一次」约束的是"获得"）。
            return [];
        }

        var granted = new CharacterId(context.Choice ?? string.Empty);
        if (!PhilosopherAbility.SelectableCharacters().Contains(granted))
        {
            throw new InvalidOperationException(
                $"哲学家选择的角色不在「镇民 / 外来者」可选集里：{context.Choice}");
        }

        var events = new List<GameEvent>();

        // 二次获得 = 替换（R-0053）：先终止旧授予——旧醉酒目标随之清醒（常驻醉酒来源按新授予重算）；
        // 旧代行槽也会因「这份授予已不在」在入格时被显式跳过（StepSlotEntry.UnavailableReason）。
        if (PhilosopherAbility.FindGrant(context.State) is { } previous)
        {
            events.Add(new PersistentEffectTerminatedEvent
            {
                EffectId = previous.Id,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.NoLongerApplies,
                    Reason = "哲学家的「获得能力」第二次生效：按替换语义终止旧授予（R-0053）",
                },
            });
        }

        var grantEffect = new PersistentEffect
        {
            // 每局限一次：标识按代际（#2、#3…），重放稳定；旧授予已终止、不复用它的标识。
            Id = PhilosopherAbility.GrantEffectId(context.State, context.Actor),
            Source = context.Actor,
            Ability = PhilosopherAbility.GrantAbility,
            Target = context.Actor,
            SourceCharacter = PhilosopherAbility.Character,
            GrantedCharacter = granted,
        };
        events.Add(new PersistentEffectAppliedEvent { Effect = grantEffect });

        // 被获得角色的格今夜还没进入、且那一格没有行动者（角色不在场 / 持有者已死亡）时，
        // 他当夜就在那一格使用刚获得的能力（首夜能力因此在首夜就能用上；R-0036 第 1 条）。
        // 提示按「授予已在」的账构建：有些能力的提示自己会查「能力在不在」（女巫的存活人数条件）。
        var grantedState = GameStateMachine.Apply(
            context.State,
            new PersistentEffectAppliedEvent { Effect = grantEffect });
        if (NightSlotActivation.PlanGranted(
                context.Plan,
                context.SlotIndex,
                context.Actor,
                granted,
                grantedState,
                context.LastDay,
                context.Seats,
                NightActions.Default) is { } activation)
        {
            events.Add(activation);
        }

        return events;
    }
}
