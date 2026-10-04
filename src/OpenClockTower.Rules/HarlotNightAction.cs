using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 流莺的夜间行动（其他夜晚 · 黄昏）：选择一名存活玩家；若对方同意披露，流莺得知其**角色**（不含阵营），
/// 说书人还可以裁定两人当夜同死（死亡标记在黎明时宣布）。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-04 抓取）：
/// 《流莺》· 角色能力——「每个夜晚*，你要选择一名存活的玩家：如果他同意，你会得知他的角色，
/// 但是你们两个可能同时死亡。」；· 角色简介——「该玩家会做一个决定：要向流莺揭露自己的角色吗？
/// 如果揭露的话，说书人可以决定流莺和该玩家是否在今晚一起死亡」「流莺只会得知她所选的玩家的角色，
/// 而不会得知这名玩家所属的阵营」；· 运作方式——「让流莺指向任意一名玩家……唤醒被选中的玩家……
/// 该玩家需要通过点头表示是，摇头表示否……如果你决定让两名玩家一起死——将"死亡"提示标记分别
/// 放在两者的角色标记旁」；· 提示标记——放置时机「在流莺夜晚行动成功得知了一名玩家的角色后」、
/// 移除时机「在黎明时宣布死亡玩家后」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0051：① 目标集合 = 全部**存活**席位（来源没有
/// 「不能选自己 / 旅行者」的排除条款，平台不额外发明限制）；② 同意与否是**说书人裁定点**——
/// 线下由说书人唤醒被选中玩家询问，平台记录裁定（D-0002：平台只记录与转达，不替说书人拍板）；
/// ③ 「两人今晚同死」与同意合并在同一条裁定里（三选一：拒绝 / 同意 / 同意且同死），说书人只答一次；
/// ④ 能力未生效（醉酒 / 中毒）时展示照常进行、信息可能为假（内容取说书人选的角色标记），
/// 且**不产生**同死（能力不生效 → 不落任何效果，百科《重要细节》三-3）；⑤ 死亡是真实夜死，
/// 走黎明公告（R-0022 / R-0045）。
/// </para>
/// <para>
/// 首个夜晚不行动（《夜晚行动顺序一览》· 2026-10-04 抓取 · 首个夜晚黄昏行只列咖啡师）；
/// 夜间顺序表上的位置见 <see cref="NightOrderTable"/>。
/// </para>
/// </remarks>
internal sealed class HarlotNightAction : INightAction, IAbilityResolution
{
    /// <summary>流莺的角色标识（建表与结算共用）。</summary>
    internal static readonly CharacterId Harlot = new("harlot");

    /// <summary>夜访能力标识（进能力账与失效账本）。</summary>
    internal static readonly AbilityId VisitAbility = new("harlot");

    /// <summary>裁定值：被选中的玩家拒绝披露——无事发生（《流莺》· 运作方式）。</summary>
    internal const string Refuse = "refuse";

    /// <summary>裁定值：同意披露、说书人不让两人同死。</summary>
    internal const string Agree = "agree";

    /// <summary>裁定值：同意披露、说书人裁定两人今晚同死。</summary>
    internal const string AgreeAndDie = "agree-kill";

    /// <summary>
    /// 能力未生效时「同意」的裁定值前缀：<c>agree:{角色 slug}</c>——说书人挑一个角色标记展示给流莺
    /// （信息可能为假，平台不生成也不校验真假）。
    /// </summary>
    internal const string AgreeWithTokenPrefix = "agree:";

    /// <inheritdoc />
    public CharacterId Character => Harlot;

    /// <inheritdoc />
    public AbilityId Ability => VisitAbility;

    /// <summary>
    /// 目标集合：全部存活席位（含流莺自己——来源只写「一名存活的玩家」，没有排除条款）。
    /// 生死未观测的席位不进候选：不猜（D-0015）。
    /// </summary>
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Seats
            .Where(seat => context.State.Seat(seat)?.LifeValue == LifeState.Alive)
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "流莺选择一名存活玩家：如果他同意，你会得知他的角色（不含阵营），"
                + "但是你们两个可能同时死亡（百科《流莺》· 2026-10-04 抓取 · 角色能力；R-0051）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// 同意与否由说书人按线下流程收集后录入（R-0051 第 2 条）：被选中的玩家不在自己的设备上作答。
    /// 能力生效时三个选项 = 拒绝 / 同意 / 同意且同死；能力未生效时改列「按角色标记展示」的选项，
    /// 且不提供同死（无效果可落）。
    /// </remarks>
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var target = RequireTarget(context);
        var character = RequireCharacter(context, target);
        var display = Display(character);

        if (context.Outcome.Effective)
        {
            return new ChoicePrompt
            {
                Context = $"流莺（{context.Actor.Value} 号）选择了 {target.Value} 号玩家"
                    + $"（按账推演：他的角色是「{display}」）。请按线下流程唤醒该玩家并询问是否披露："
                    + "拒绝则无事发生；同意则向他展示……随后唤醒流莺、把真实角色标记展示给她。"
                    + "你还可以裁定两人今晚同死——死亡标记在黎明时宣布"
                    + "（百科《流莺》· 2026-10-04 抓取 · 运作方式 / 提示标记；R-0051）。",
                Options =
                [
                    new DecisionOption
                    {
                        Value = Refuse,
                        Preview = "拒绝：该玩家没有披露角色，无事发生",
                    },
                    new DecisionOption
                    {
                        Value = Agree,
                        Preview = $"同意：把真实角色「{display}」展示给流莺（两人今晚都不因此死亡）",
                    },
                    new DecisionOption
                    {
                        Value = AgreeAndDie,
                        Preview = $"同意：展示真实角色「{display}」，并让两人今晚同死（黎明时宣布）",
                    },
                ],
                OnNoOption = NoOptionBehavior.BlockAndAlert,
            };
        }

        return new ChoicePrompt
        {
            Context = $"流莺（{context.Actor.Value} 号）的能力未生效（{context.Outcome.Note}）："
                + "询问与展示照常进行，但信息可能为假，且不会产生同死。请选择要展示给她的角色标记"
                + $"（按账推演：{target.Value} 号玩家的真实角色是「{display}」；"
                + "百科《重要细节》· 2026-10-01 抓取 · 三-3；R-0051）。",
            Options =
            [
                new DecisionOption
                {
                    Value = Refuse,
                    Preview = "拒绝：该玩家没有披露角色，无事发生",
                },
                .. SectsAndVioletsRoster.All.Select(candidate => new DecisionOption
                {
                    Value = FormatAgreeWithToken(candidate),
                    Preview = $"同意：展示「{Display(candidate)}」标记（能力未生效——内容可能为假，不产生同死）",
                }),
            ],
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Decision))
        {
            throw new InvalidOperationException("流莺的夜访必须由说书人裁定（同意 / 拒绝 / 同死），不能是空的");
        }

        var target = RequireTarget(context);
        RequireAlive(context, target, "被选中的玩家");

        if (string.Equals(context.Decision, Refuse, StringComparison.Ordinal))
        {
            return [];
        }

        if (!context.Outcome.Effective)
        {
            // 能力未生效：展示的角色标记由说书人挑（内容可能为假），不落任何死亡（R-0051 第 4 条）。
            var token = ParseAgreeWithToken(context.Decision);
            var content = Reveal(target, Display(token));
            return
            [
                new InformationResultIssuedEvent
                {
                    Recipient = context.Actor,
                    Ability = Ability,
                    Content = content,
                    MayBeFalse = true,
                    Note = $"流莺的能力未生效（{context.Outcome.Note}）：展示的角色标记由说书人给出，"
                        + "内容可能为假（百科《重要细节》· 2026-10-01 抓取 · 三-3；R-0051）",
                },
            ];
        }

        if (context.Decision is not (Agree or AgreeAndDie))
        {
            throw new InvalidOperationException(
                $"流莺的裁定只能是 {Refuse} / {Agree} / {AgreeAndDie}：{context.Decision}");
        }

        var trueCharacter = RequireCharacter(context, target);
        var events = new List<GameEvent>
        {
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = Reveal(target, Display(trueCharacter)),
                MayBeFalse = false,
                Note = "流莺得知被选中玩家的**角色**（不含阵营）；能力生效，信息为真"
                    + "（百科《流莺》· 2026-10-04 抓取 · 角色简介；R-0051）",
            },
        };

        if (string.Equals(context.Decision, AgreeAndDie, StringComparison.Ordinal))
        {
            // 「说书人决定让两名玩家一起死」：真实夜死、黎明公告（R-0022 / R-0045）。
            // 目标可以是流莺自己（来源没有排除条款）：同一个人只落一条死亡事实，不重复记账。
            events.AddRange(Death(context, context.Actor, "流莺夜访：说书人裁定两人今晚同死（流莺本人）"));
            if (target != context.Actor)
            {
                events.AddRange(Death(context, target, $"流莺夜访：说书人裁定两人今晚同死（{target.Value} 号）"));
            }
        }

        return events;
    }

    /// <summary>解析并校验目标席位（形状 + 在本局座次名单里）；生死由 <see cref="RequireAlive"/> 单独校验。</summary>
    private static SeatId RequireTarget(AbilityResolutionContext context)
    {
        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"流莺的结算缺少合法目标席位：{context.Choice}");

        if (!context.Seats.Contains(target))
        {
            throw new InvalidOperationException($"流莺选择的目标席位 {target.Value} 不在本局座次名单里");
        }

        return target;
    }

    /// <summary>该席位此刻的角色；未观测时显式失败（不猜，D-0015）。</summary>
    private static CharacterId RequireCharacter(AbilityResolutionContext context, SeatId seat) =>
        context.State.Seat(seat)?.CharacterValue
        ?? throw new InvalidOperationException($"席位 {seat.Value} 的角色尚未观测：流莺的夜访不替它猜");

    /// <summary>该席位此刻必须存活（来源：选择一名**存活**的玩家）；未观测 / 已死亡都显式失败。</summary>
    private static void RequireAlive(AbilityResolutionContext context, SeatId seat, string what)
    {
        var life = context.State.Seat(seat)?.LifeValue
            ?? throw new InvalidOperationException($"席位 {seat.Value}（{what}）的生死尚未观测，流莺的夜访不替它猜");

        if (life != LifeState.Alive)
        {
            throw new InvalidOperationException(
                $"席位 {seat.Value}（{what}）此刻不是存活状态：流莺的目标必须是存活玩家，本次夜访无法结算");
        }
    }

    /// <summary>把说书人选的角色标记拼成给流莺的信息（只报角色、不报阵营）。</summary>
    private static string Reveal(SeatId target, string display) =>
        $"{target.Value} 号玩家的角色是「{display}」";

    /// <summary>角色中文名；不在花名册里时回显 slug，不吞。</summary>
    private static string Display(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;

    /// <summary>能力未生效时的「同意」裁定值编码。</summary>
    private static string FormatAgreeWithToken(CharacterId character) =>
        string.Concat(AgreeWithTokenPrefix, character.Value);

    /// <summary>解析「同意 + 角色标记」裁定值；形状不对或角色不在花名册里一律显式失败。</summary>
    private static CharacterId ParseAgreeWithToken(string decision)
    {
        if (!decision.StartsWith(AgreeWithTokenPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"流莺的能力未生效时，裁定只能是 {Refuse} 或 {AgreeWithTokenPrefix}{{角色 slug}}：{decision}");
        }

        var character = new CharacterId(decision[AgreeWithTokenPrefix.Length..]);
        if (!SectsAndVioletsRoster.Contains(character))
        {
            throw new InvalidOperationException($"流莺展示的角色标记 {character.Value} 不在首版花名册里");
        }

        return character;
    }

    /// <summary>
    /// 一次夜访致死：即时型效果（归因）+ 死亡事实。夜死累积到黎明公告（R-0022），
    /// 与恶魔击杀走同一族事件；不经过 <see cref="NightKill"/>——那里的窗口针对恶魔攻击（R-0030）。
    /// </summary>
    private static IReadOnlyList<GameEvent> Death(AbilityResolutionContext context, SeatId seat, string reason)
    {
        var effectId = new EffectId($"{context.SlotKey}:harlot-death:{seat.Value}");
        return
        [
            new InstantaneousEffectAppliedEvent
            {
                Effect = new InstantaneousEffect
                {
                    Id = effectId,
                    Source = context.Actor,
                    Ability = VisitAbility,
                    Target = seat,
                },
            },
            new SeatStateChangedEvent
            {
                Seat = seat,
                Life = LifeState.Dead,
                Reason = reason,
                CausedBy = context.Actor,
                EffectId = effectId,
            },
        ];
    }
}
