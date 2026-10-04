using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 钟表匠的夜间行动契约：不产生玩家选择，由说书人给出本夜的最小距离。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《钟表匠》· 2026-10-01 抓取 · 规则细节（L1）：
/// 「当钟表匠即将被唤醒之前，说书人会根据当前场上的情况给出实时的信息」；
/// 「在计算距离时，距离值等同于：恶魔与爪牙这两名玩家之间的玩家数量+1」。
/// </para>
/// <para>
/// 平台不替说书人算（D-0002）：本契约产出**说书人裁定点**（无玩家选项 + StorytellerDecides），
/// 裁定结果原样记成信息结果，下发给钟表匠本人；能力未生效时标「可能错误」（只说书人可见）。
/// </para>
/// </remarks>
internal sealed class ClockmakerNightAction : INightAction, IAbilityResolution
{
    /// <summary>钟表匠信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("clockmaker");

    private static readonly CharacterId Clockmaker = new("clockmaker");

    /// <inheritdoc />
    public CharacterId Character => Clockmaker;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ChoicePrompt
        {
            Context = "钟表匠获得信息：说书人给出本夜的最小距离（恶魔与最近爪牙之间的人数 + 1）"
                + (VortoxInterference.IsActiveFor(context.State, context.Actor)
                    ? "。涡流在场：这条信息必须为假（R-0028）"
                    : string.Empty),
            Options = [],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Decision))
        {
            throw new InvalidOperationException("钟表匠的信息必须由说书人裁定给出，不能是空的");
        }

        return
        [
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = context.Decision,
                MayBeFalse = !context.Outcome.Effective || VortoxInterference.IsActiveFor(context.State, context.Actor),
                Note = VortoxInterference.NoteFor(
                    context.State,
                    context.Actor,
                    context.Outcome.Effective
                        ? "说书人按场上情况算出的实时信息"
                        : context.Outcome.Note),
            },
        ];
    }
}
