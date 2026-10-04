using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 咖啡师的黄昏行动：说书人二选一——① 一名玩家「清醒且健康」（免疫醉酒 / 中毒、必定获得正确信息）；
/// ② 一名玩家「行动两次」（能力可以再结算一次）。两个效果都持续到**下个黄昏**，
/// 且受影响玩家会得知是哪一个效果。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-04 抓取）：
/// 《咖啡师》· 角色能力——「每个夜晚，直至下个黄昏，由说书人二选一：1）一名玩家解除并免受醉酒和
/// 中毒影响，且会得知正确信息；2）一名玩家的能力可以生效两次。该玩家会得知是哪个效果。」；
/// · 角色简介——「每个夜晚由说书人选择咖啡师的角色能力生效，和受到该能力影响的玩家。咖啡师不会
/// 知道说书人选择了谁或者哪一项能力，但受影响的玩家会知道。」；· 运作方式——「一个被标记了
/// 『清醒且健康』的玩家会变得清醒且健康（即使他同时被标记了醉酒或中毒）而且总是会得到正确信息
/// （即使有能力会使他醉酒或中毒）」「一个被标记了『行动两次』的玩家能在合适时机使用两次能力」；
/// · 提示标记——「由说书人选择**任意一名玩家**，在该玩家角色标记旁放置咖啡师的这两种标记之一」
/// 「在触发咖啡师下一晚的效果之前，将上一夜的所有咖啡师的标记都移除」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0047（免疫窗口的账本语义）与 R-0052（两效果的
/// 平台收口）：目标集合 = 本局全部在局席位（「任意一名玩家」，不额外发明排除条款；死在席位上的
/// 玩家同样可以被标记——呆瓜范例即"被标记后死亡、死亡选择照常两次"）；二选一是**同一名玩家的
/// 一条原子裁定**（目标 × 效果一次给全，不拆成两次请求）；能力未生效（醉酒 / 中毒 / 死亡）时
/// 不落任何效果、也不向目标宣告（两个效果都是"让能力更有效"，百科《重要细节》三-3）。
/// </para>
/// <para>
/// 本契约是**说书人受众**（<see cref="ChoiceAudience.Storyteller"/>）——选项就是候选
/// （效果 × 席位），说书人点选后值原样进入结算；咖啡师玩家自己不收到任何请求。
/// 夜序位置见 <see cref="NightOrderTable"/>（首夜与其他夜晚的黄昏步之后）。
/// </para>
/// </remarks>
internal sealed class BaristaNightAction : INightAction, IAbilityResolution
{
    /// <summary>裁定值前缀：效果 1「清醒且健康」（值形如 <c>healthy:seat:3</c>）。</summary>
    internal const string HealthyPrefix = "healthy";

    /// <summary>裁定值前缀：效果 2「行动两次」（值形如 <c>twice:seat:3</c>）。</summary>
    internal const string SecondActionPrefix = "twice";

    /// <inheritdoc />
    public CharacterId Character => BaristaAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => BaristaAbility.Ability;

    /// <summary>
    /// 说书人的二选一：候选 = 每个在局席位 × 两个效果（效果 1 / 2），席位账上的生死只影响预览标注，
    /// 不影响候选集合（「任意一名玩家」；生死未观测时同样不替它猜，只如实标注）。
    /// </summary>
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new List<DecisionOption>();
        foreach (var seat in context.Seats.OrderBy(candidate => candidate.Value))
        {
            var suffix = context.State.Seat(seat)?.LifeValue switch
            {
                LifeState.Dead => "（该席位已死亡）",
                null => "（该席位生死尚未观测）",
                _ => string.Empty,
            };

            options.Add(new DecisionOption
            {
                Value = Format(EffectWindowKind.AfflictionImmunity, seat),
                Preview = $"{seat.Value} 号玩家：清醒且健康"
                    + "（直到下个黄昏免疫醉酒 / 中毒、必定获得正确信息）" + suffix,
            });
            options.Add(new DecisionOption
            {
                Value = Format(EffectWindowKind.SecondAction, seat),
                Preview = $"{seat.Value} 号玩家：行动两次"
                    + "（直到下个黄昏，他的能力可以生效两次）" + suffix,
            });
        }

        return new ChoicePrompt
        {
            Context = "咖啡师（旅行者）：说书人选择一名玩家与一个效果（二选一），持续到下个黄昏——"
                + "① 清醒且健康：该玩家解除并免受醉酒 / 中毒影响，且必定获得正确信息；"
                + "② 行动两次：该玩家的能力可以生效两次。受影响玩家会得知是哪一个效果；"
                + "咖啡师不会知道（百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介；R-0052）",
            Options = options,
            Audience = ChoiceAudience.Storyteller,
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
    }

    /// <inheritdoc />
    /// <remarks>二选一在槽位入口一并给出（说书人受众），选择后直接结算，不再补一次裁定。</remarks>
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Decision))
        {
            throw new InvalidOperationException(
                "咖啡师的二选一必须由说书人裁定（目标 + 效果），不能是空的（R-0052 第 1 条）");
        }

        var (kind, target) = ParseDecision(context.Decision);
        if (!context.Seats.Contains(target))
        {
            throw new InvalidOperationException($"咖啡师选择的目标席位 {target.Value} 不在本局座次名单里");
        }

        // 能力未生效（醉酒 / 中毒 / 死亡）：不落任何效果、也不向目标宣告——两个效果都是"让能力更有效"，
        // 未生效时按《重要细节》三-3 无事发生（R-0052 第 6 条）。
        if (!context.Outcome.Effective)
        {
            return [];
        }

        var events = new List<GameEvent>
        {
            new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId($"{context.SlotKey}:barista:{SlugOf(kind)}:{target.Value}"),
                    Source = context.Actor,
                    Ability = BaristaAbility.Ability,
                    Target = target,
                    SourceCharacter = context.ActorCharacter,
                    Window = kind,
                },
            },
            new InformationResultIssuedEvent
            {
                Recipient = target,
                Ability = BaristaAbility.Ability,
                Content = Announcement(kind, context.Actor),
                MayBeFalse = false,
                Note = "咖啡师的效果生效：告知受影响玩家是哪一个效果"
                    + "（百科《咖啡师》· 2026-10-04 抓取 · 角色能力；R-0052 第 5 条）",
            },
        };

        // 效果 2 的当夜落点：目标本夜可能已经因「每局限一次」被计划判成「本夜无行动」——
        // 窗口让他重新可用，把那一格重开成真实提示（R-0052 第 3 条；过时不候由 PlanUpgrade 守）。
        if (kind == EffectWindowKind.SecondAction && UpgradeOf(context, target) is { } upgrade)
        {
            events.Add(upgrade);
        }

        return events;
    }

    /// <summary>
    /// 「行动两次」的当夜落点：把目标本夜那张「本夜无行动」的角色格重开成真实提示。
    /// </summary>
    /// <remarks>
    /// 只对**存活**目标重开（死亡席位本夜不行动；呆瓜那类死亡触发的能力不走槽位、由触发层负责）；
    /// 目标的角色未观测、契约未实现、或契约声明不支持二次结算（哲学家的「获得能力」，R-0053 Open）
    /// 时不重开——理由都在那一格自己的跳过记录里显式可见，不静默改行为。
    /// </remarks>
    private static SlotActivatedEvent? UpgradeOf(AbilityResolutionContext context, SeatId target)
    {
        var entry = context.State.Seat(target);
        if (entry?.LifeValue != LifeState.Alive || entry.CharacterValue is not { } targetCharacter)
        {
            return null;
        }

        if (NightActions.Resolutions.Find(targetCharacter)?.SupportsSecondAction != true)
        {
            return null;
        }

        return NightSlotActivation.PlanUpgrade(
            context.Plan,
            context.SlotIndex,
            target,
            targetCharacter,
            context.State,
            context.LastDay,
            context.Seats,
            NightActions.Default);
    }

    /// <summary>裁定值编码：<c>{效果}:seat:{席位号}</c>（与 <see cref="SeatChoice"/> 同一席位编码）。</summary>
    private static string Format(EffectWindowKind kind, SeatId seat) =>
        string.Concat(SlugOf(kind), ":", SeatChoice.Format(seat));

    /// <summary>效果 → 裁定值前缀；未知窗口一律显式失败（不吞）。</summary>
    private static string SlugOf(EffectWindowKind kind) => kind switch
    {
        EffectWindowKind.AfflictionImmunity => HealthyPrefix,
        EffectWindowKind.SecondAction => SecondActionPrefix,
        _ => throw new InvalidOperationException($"咖啡师没有这种效果窗口：{kind}"),
    };

    /// <summary>解析并校验裁定：形状（效果 + 席位）与取值都显式失败，不当成"随便选一个"。</summary>
    private static (EffectWindowKind Kind, SeatId Seat) ParseDecision(string decision)
    {
        var separator = decision.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == decision.Length - 1)
        {
            throw new InvalidOperationException(
                $"咖啡师的裁定形状必须是 {HealthyPrefix}|{SecondActionPrefix}:seat:{{席位号}}：{decision}");
        }

        var kind = decision[..separator] switch
        {
            HealthyPrefix => EffectWindowKind.AfflictionImmunity,
            SecondActionPrefix => EffectWindowKind.SecondAction,
            var other => throw new InvalidOperationException(
                $"咖啡师的裁定效果只能是 {HealthyPrefix} / {SecondActionPrefix}：{other}"),
        };

        var seat = SeatChoice.Parse(decision[(separator + 1)..])
            ?? throw new InvalidOperationException($"咖啡师的裁定目标不是合法席位：{decision}");

        return (kind, seat);
    }

    /// <summary>给受影响玩家的宣告：只说「哪一个效果」（来源：角色能力末句）。</summary>
    private static string Announcement(EffectWindowKind kind, SeatId barista) => kind switch
    {
        EffectWindowKind.AfflictionImmunity =>
            $"{barista.Value} 号咖啡师对你使用了「清醒且健康」效果：直到下个黄昏，你解除并免受醉酒和"
            + "中毒影响，并且你获得的信息一定正确（即使涡流在场）"
            + "（百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介；R-0047）",
        EffectWindowKind.SecondAction =>
            $"{barista.Value} 号咖啡师对你使用了「行动两次」效果：直到下个黄昏，你的能力可以生效两次"
            + "（百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介；R-0052）",
        _ => throw new InvalidOperationException($"咖啡师没有这种效果窗口：{kind}"),
    };
}
