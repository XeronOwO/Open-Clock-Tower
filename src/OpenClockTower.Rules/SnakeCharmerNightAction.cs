using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 舞蛇人的夜间行动：每个夜晚（含首夜）选择一名存活玩家；选中恶魔时双方交换角色与阵营，
/// 原恶魔变成永久中毒的舞蛇人。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《舞蛇人》· 2026-10-01 抓取 · 角色能力——「每个夜晚，你要选择一名存活玩家：
/// 如果你选中了恶魔，你和他交换角色和阵营，然后他中毒」；· 运作方式——让舞蛇人指向任意一名
/// 存活玩家、「如果被选择的玩家是恶魔，旧的舞蛇人变为新的（邪恶）恶魔，并且旧的恶魔变为新的
/// （善良）舞蛇人」「在舞蛇人属于邪恶阵营，或者恶魔属于善良阵营的特殊情况下，依旧相应地交换
/// 他们的阵营」「在新的舞蛇人角色标记旁放置『中毒』提示标记」；· 范例——「如果具有舞蛇人能力的
/// 哲学家选中了恶魔玩家并变成了新的恶魔，原本的恶魔会变成中毒的哲学家」（能力获得者不变成
/// 舞蛇人，只交换持有者双方的**角色**，中毒归新角色自己）；《夜晚行动顺序一览》· 2026-10-01 抓取 ·
/// 舞蛇人条——「唤醒舞蛇人，让他选择一名存活玩家。如果选中恶魔则执行角色和阵营的交换，
/// 并在舞蛇人入睡后通知旧恶魔角色变化」。
/// </para>
/// <para>
/// 三条平台口径（见 <c>docs/standard/rulings.md</c>）：
/// ① 交换后的永久中毒不随来源（被换者自己）的醉酒 / 中毒挂起，只随该角色死亡 / 离场终止（R-0031）；
/// ② 换手后尚未进入的角色槽位重绑到新持有者（新恶魔当夜仍按槽位行动）（R-0032）；
/// ③ 选到非恶魔时没有显式信息结果——「得知对方不是恶魔」是行动仪式本身带来的隐含事实，
/// 不是一条由说书人裁定的信息（本角色不属于「获取信息」类）。阵营 / 角色变化对本人告知走
/// 说书人外部语音（同开局分配），玩家投影暂不设角色出口（`done/settlement-engine.md` 残余 3）。
/// </para>
/// </remarks>
internal sealed class SnakeCharmerNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => SnakeCharmerAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => SnakeCharmerAbility.ActionAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new List<DecisionOption>();
        foreach (var seat in context.Seats.OrderBy(seat => seat.Value))
        {
            switch (context.State.Seat(seat)?.LifeValue)
            {
                case LifeState.Alive:
                    options.Add(new DecisionOption
                    {
                        Value = SeatChoice.Format(seat),
                        Preview = $"{seat.Value} 号玩家",
                    });
                    break;

                case null:
                    // 生死未观测：算不出「存活玩家」集合——显式阻塞，不猜（R-0009 / D-0015）。
                    return new ChoicePrompt
                    {
                        Context = $"席位 {seat.Value} 的生死尚未观测：舞蛇人算不出「存活玩家」集合，不替它猜",
                        Options = [],
                        OnNoOption = NoOptionBehavior.BlockAndAlert,
                    };

                default:
                    break;
            }
        }

        return new ChoicePrompt
        {
            Context = "舞蛇人选择一名存活玩家（含自己）：选中恶魔时交换角色与阵营，"
                + "原恶魔变成永久中毒的舞蛇人（百科《舞蛇人》· 2026-10-01 抓取 · 角色能力 / 运作方式）",
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
            // 中毒 / 醉酒 / 死亡：能力不生效——不交换、不落中毒，也不允许伪造变化
            // （百科《中毒》· 2026-10-01 抓取 · 合理欺骗：「不应该、也不能在他们中毒醉酒时欺骗他们
            // 阵营或角色发生了变化」）。
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"舞蛇人的结算缺少合法目标席位：{context.Choice}");

        var targetState = context.State.Seat(target)
            ?? throw new InvalidOperationException($"舞蛇人选择的目标席位 {target.Value} 不在状态账里");
        var actorState = context.State.Seat(context.Actor)
            ?? throw new InvalidOperationException($"舞蛇人的行动者席位 {context.Actor.Value} 不在状态账里");

        if (targetState.LifeValue != LifeState.Alive)
        {
            // 能力原文是「选择一名存活玩家」；请求挂起期间目标死亡（说书人上报等）时这条答案失去意义。
            // 整条命令失败、请求保持挂起，玩家可以重选——对应实体桌上说书人摇头示意（《重要细节》六）。
            throw new InvalidOperationException(
                $"舞蛇人选择的目标（{target.Value} 号）在结算时已不是存活状态：本答案无效，请重新选择");
        }

        var targetCharacter = targetState.CharacterValue
            ?? throw new InvalidOperationException($"舞蛇人目标席位 {target.Value} 的角色尚未观测，判不了是不是恶魔");
        if (SectsAndVioletsRoster.TypeOf(targetCharacter) != CharacterType.Demon)
        {
            // 「如果被选择的玩家不是恶魔，则无事发生」——能力照常记「已使用且生效」，只是没有状态变化。
            return [];
        }

        var actorAlignment = actorState.Alignment?.Value
            ?? throw new InvalidOperationException($"舞蛇人行动者席位 {context.Actor.Value} 的阵营尚未观测，换不了阵营");
        var targetAlignment = targetState.Alignment?.Value
            ?? throw new InvalidOperationException($"舞蛇人目标席位 {target.Value} 的阵营尚未观测，换不了阵营");

        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                // 舞蛇人 → 恶魔：角色与阵营同时交换（百科明文例外；机制 11「依旧相应地交换他们的阵营」）。
                Seat = context.Actor,
                Character = targetCharacter,
                Alignment = targetAlignment,
                Reason = SnakeCharmerAbility.SwapReason,
                CausedBy = context.Actor,
            },
            new SeatStateChangedEvent
            {
                // 恶魔 → 舞蛇人：同样交换角色与阵营；永久中毒随后作为持续型效果落在这一席。
                Seat = target,
                Character = SnakeCharmerAbility.Character,
                Alignment = actorAlignment,
                Reason = SnakeCharmerAbility.SwapReason,
                CausedBy = context.Actor,
            },
        };

        // 角色在夜里换手：恶魔尚未进入的那一格要跟着新持有者走，否则新恶魔当夜动不了（R-0032）。
        if (NightSlotActivation.Plan(
                context.Plan,
                context.SlotIndex,
                context.Actor,
                targetCharacter,
                context.State,
                context.Seats,
                NightActions.Default) is { } rebound)
        {
            events.Add(rebound);
        }

        // 原恶魔（现舞蛇人）永久中毒：来源 = 它自己（提示标记附注「让这个角色中毒的效果是来自于
        // 这个角色本身」），只随该角色死亡 / 离场终止（R-0031）。
        events.Add(new PersistentEffectAppliedEvent
        {
            Effect = new PersistentEffect
            {
                Id = SnakeCharmerAbility.PoisonEffectId(context.PlanLabel, context.SlotId),
                Source = target,
                Ability = SnakeCharmerAbility.PoisonAbility,
                Target = target,
                SourceCharacter = SnakeCharmerAbility.Character,
                Dimension = EffectDimension.Poison,
                SourceStateIndependent = true,
            },
        });

        return events;
    }
}
