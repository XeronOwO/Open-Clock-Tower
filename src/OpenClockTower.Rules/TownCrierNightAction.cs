using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 城镇公告员的夜间行动：说书人给出「今天有没有爪牙发起过提名」。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《城镇公告员》· 2026-10-01 抓取 · 角色简介 1–3 / 运作方式 4–5（该页「规则细节」为「无」，
/// 按 R-0011 标 L4）：「城镇公告员得知爪牙是否提名」「他不会得知哪些玩家是爪牙或有多少爪牙进行了提名，
/// 只会得知今天白天是否有爪牙发起了提名」「除了第一个夜晚的每个夜晚都唤醒城镇公告员」。
/// </para>
/// <para>
/// 平台口径（R-0037）：读当天白天账里的**提名**，按提名发生时的**提名者角色快照**判定——
/// 任何一条提名的发起者当时是爪牙 ⇒ 是。平台只推演，信息由说书人给出（D-0002）；
/// 涡流在场时必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class TownCrierNightAction : INightAction, IAbilityResolution
{
    /// <summary>城镇公告员信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("town-crier");

    private static readonly CharacterId TownCrier = new("town-crier");

    /// <inheritdoc />
    public CharacterId Character => TownCrier;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reading = RetrospectiveReadings.MinionNominated(context.LastDay);

        return new ChoicePrompt
        {
            Context = "城镇公告员获得信息：说书人告诉『今天是否有爪牙发起过提名』"
                + "（百科《城镇公告员》· 2026-10-01 抓取 · 角色简介）。"
                + $"按白天账推演：{RetrospectiveReadings.Describe(reading)}"
                + (reading is null ? "——记录读不出来，请说书人按现场判断。" : "。")
                + (VortoxInterference.IsActive(context.State)
                    ? "涡流在场：这条信息必须为假（R-0028）——给出与推演相反的说法。"
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
            throw new InvalidOperationException("城镇公告员的信息必须由说书人给出，不能是空的");
        }

        var reading = RetrospectiveReadings.MinionNominated(context.LastDay);
        return
        [
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = context.Decision,
                MayBeFalse = !context.Outcome.Effective || VortoxInterference.IsActive(context.State),
                Note = VortoxInterference.NoteFor(
                    context.State,
                    context.Outcome.Effective
                        ? $"平台按白天账推演：{RetrospectiveReadings.Describe(reading)}"
                            + "（R-0037：按提名动作的角色快照）；信息由说书人给出"
                        : context.Outcome.Note),
            },
        ];
    }
}
