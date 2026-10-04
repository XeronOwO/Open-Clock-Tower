using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 神谕者的夜间行动：说书人给出「当前有多少名死亡的邪恶玩家」。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《神谕者》· 2026-10-01 抓取 · 角色简介 1–4 / 运作方式 5（该页无「规则细节」小节，
/// 按 R-0011 标 L4）：「神谕者会得知有多少死亡玩家是邪恶的」「由于神谕者每晚在恶魔之后行动，
/// 所以神谕者的信息以当晚黎明时的状态为基准」「能检测死去的爪牙和恶魔，以及任何属于邪恶阵营的玩家」
/// 「计算在魔典中倒置的镇民和外来者角色标记，因为这意味着他们现在属于邪恶阵营」
/// 「除了第一个夜晚的每个夜晚，唤醒神谕者」。
/// </para>
/// <para>
/// 平台口径：数**当前账**里「死亡且邪恶」的席位——她在顺序表上排在恶魔之后，当夜的死亡已经记账；
/// 按当前阵营算（换过阵营的按新阵营）。平台只推演，信息由说书人给出（D-0002）；
/// 涡流在场时必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class OracleNightAction : INightAction, IAbilityResolution
{
    /// <summary>神谕者信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("oracle");

    private static readonly CharacterId Oracle = new("oracle");

    /// <inheritdoc />
    public CharacterId Character => Oracle;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reading = RetrospectiveReadings.DeadEvilCount(context.State, context.Seats);

        return new ChoicePrompt
        {
            Context = "神谕者获得信息：说书人比划『当前死亡且邪恶的玩家数量』"
                + "（百科《神谕者》· 2026-10-01 抓取 · 角色简介；按当前阵营，含当夜死者）。"
                + $"按当前账推演：{RetrospectiveReadings.Describe(reading)}"
                + (reading is null ? "——有席位的生死 / 阵营未观测，读不出来，请说书人按现场判断。" : "。")
                + (VortoxInterference.IsActiveFor(context.State, context.Actor)
                    ? "涡流在场：这条信息必须为假（R-0028）——给出与推演不同的数字。"
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
            throw new InvalidOperationException("神谕者的数字必须由说书人给出，不能是空的");
        }

        var reading = RetrospectiveReadings.DeadEvilCount(context.State, context.Seats);
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
                        ? $"平台按当前账推演：{RetrospectiveReadings.Describe(reading)}"
                            + "（当前阵营口径，含当夜死者）；信息由说书人给出"
                        : context.Outcome.Note),
            },
        ];
    }
}
