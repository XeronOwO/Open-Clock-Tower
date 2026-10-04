using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 女裁缝的夜间行动：每夜可选两名其他玩家，得知他们是否属于同一阵营；每局限一次，摇头可不用。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《女裁缝》· 2026-10-01 抓取 · 角色简介 1–3 / 运作方式 1–6——
/// 「女裁缝能得知两名玩家是否属于同一阵营」「每场游戏只能得到一次信息」
/// 「可以选择除自己外的任何玩家，无论他是生是死」「每个夜晚，唤醒女裁缝。她要么摇头不使用能力，
/// 要么指向另两名玩家」「在女裁缝使用能力后，为她放置"失去能力"提示标记，并从夜晚顺序表上移除她的夜晚标记」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0040：使用（含未生效）即记一次能力账、
/// 由账本派生「失去能力」标记；摇头不用不记账、之后夜晚仍可再选；用过之后夜晚建表不再唤醒她
/// （空槽照走配额，D-0013 §1）。信息内容由说书人裁定（是 / 否），未生效时标「可能为假」（三-1 / 三-3）；
/// 涡流在场时必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class SeamstressNightAction : INightAction, IAbilityResolution
{
    /// <summary>女裁缝信息能力标识（进能力使用账本与失效账本）。</summary>
    internal static readonly AbilityId InfoAbility = new("seamstress");

    /// <summary>角色标识（建表期按账判定是否已用尽也要读它）。</summary>
    internal static readonly CharacterId Seamstress = new("seamstress");

    /// <summary>摇头不使用能力（百科《女裁缝》· 运作方式 5：如果她摇头，无事发生）。</summary>
    internal const string Decline = "decline";

    /// <summary>说书人裁定「是」：两人属于同一阵营。</summary>
    internal const string AnswerYes = "yes";

    /// <summary>说书人裁定「否」：两人不属于同一阵营。</summary>
    internal const string AnswerNo = "no";

    /// <inheritdoc />
    public CharacterId Character => Seamstress;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <summary>摇头不用不算使用：之后夜晚仍可再选（R-0040）。</summary>
    public bool CountsAsUse(AbilityResolutionContext context) =>
        !string.Equals(context.Choice, Decline, StringComparison.Ordinal);

    /// <summary>
    /// 「每局限一次」：咖啡师「行动两次」窗口内把上限放宽到**总使用次数 2**（R-0052 第 3 条）——
    /// 用过一次还能再用一次；一次没用过则下个黄昏前可以合计用两次。
    /// </summary>
    public bool IsLimitedPerGame => true;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var others = context.Seats
            .Where(seat => seat != context.Actor)
            .OrderBy(seat => seat.Value)
            .ToArray();

        var options = new List<DecisionOption>();
        for (var first = 0; first < others.Length; first++)
        {
            for (var second = first + 1; second < others.Length; second++)
            {
                options.Add(new DecisionOption
                {
                    Value = PlayerPairChoice.FormatPair(others[first], others[second]),
                    Preview = $"{others[first].Value} 号与 {others[second].Value} 号",
                });
            }
        }

        options.Add(new DecisionOption
        {
            Value = Decline,
            Preview = "摇头：本夜不使用能力（每局限一次，之后仍可再用）",
        });

        return new ChoicePrompt
        {
            Context = "女裁缝选择两名其他玩家（可含已死亡），得知他们是否属于同一阵营；"
                + "或摇头不用（百科《女裁缝》· 2026-10-01 抓取 · 角色简介 / 运作方式；R-0040）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.Choice, Decline, StringComparison.Ordinal))
        {
            // 摇头：无事发生，直接结算（不记账、不裁定）。
            return null;
        }

        var pair = PlayerPairChoice.ParsePair(context.Choice)
            ?? throw new InvalidOperationException($"女裁缝的结算缺少合法玩家对：{context.Choice}");

        var contextText = $"女裁缝选择了 {pair.First.Value} 号与 {pair.Second.Value} 号。"
            + DescribeLean(context, pair)
            + " 请裁定要告诉她的信息：";
        if (VortoxInterference.IsActiveFor(context.State, context.Actor))
        {
            contextText += " 涡流在场：这条信息必须为假（R-0028），请选与真实关系相反的一项。";
        }

        return new ChoicePrompt
        {
            Context = contextText,
            Options =
            [
                new DecisionOption { Value = AnswerYes, Preview = "是：两人属于同一阵营" },
                new DecisionOption { Value = AnswerNo, Preview = "否：两人不属于同一阵营" },
            ],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.Choice, Decline, StringComparison.Ordinal))
        {
            return [];
        }

        var pair = PlayerPairChoice.ParsePair(context.Choice)
            ?? throw new InvalidOperationException($"女裁缝的结算缺少合法玩家对：{context.Choice}");

        if (context.Decision is not (AnswerYes or AnswerNo))
        {
            throw new InvalidOperationException($"女裁缝的裁定只能是 是 / 否：{context.Decision}");
        }

        var sameSide = string.Equals(context.Decision, AnswerYes, StringComparison.Ordinal);
        var content = sameSide
            ? $"{pair.First.Value} 号与 {pair.Second.Value} 号玩家属于同一阵营"
            : $"{pair.First.Value} 号与 {pair.Second.Value} 号玩家不属于同一阵营";

        var vortox = VortoxInterference.IsActiveFor(context.State, context.Actor);
        return
        [
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = content,
                MayBeFalse = !context.Outcome.Effective || vortox,
                Note = VortoxInterference.NoteFor(
                    context.State,
                    context.Actor,
                    context.Outcome.Effective ? null : context.Outcome.Note),
            },
        ];
    }

    /// <summary>按当前账推演两人的阵营关系，写进说书人上下文（只提示、不替说书人拍板，D-0002）。</summary>
    private static string DescribeLean(AbilityResolutionContext context, (SeatId First, SeatId Second) pair)
    {
        var first = context.State.Seat(pair.First)?.Alignment?.Value;
        var second = context.State.Seat(pair.Second)?.Alignment?.Value;

        return (first, second) switch
        {
            ({ } a, { } b) => a == b
                ? "按当前账推演：两人属于同一阵营。"
                : "按当前账推演：两人不属于同一阵营。",
            _ => "按当前账推演：两人的阵营还没有观测齐，无法推演（不猜，D-0015）。",
        };
    }
}
