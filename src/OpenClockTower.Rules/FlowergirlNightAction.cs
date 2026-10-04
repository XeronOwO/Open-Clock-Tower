using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 卖花女孩的夜间行动：说书人给出「恶魔今天有没有参与投票（举手）」。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《卖花女孩》· 2026-10-01 抓取 · 角色简介 1–5 / 运作方式 6–7（该页「规则细节」为「无」，
/// 按 R-0011 标 L4）：「卖花女孩会得知恶魔是否参与了投票」「无论被提名的玩家是否被处决，
/// 恶魔的投票都会被卖花女孩计算在内」「如果在原恶魔投票之后，卖花女孩得知该信息之前，
/// 恶魔玩家发生了改变，卖花女孩的能力还是会检测到原恶魔是否投票」「除首个夜晚以外的每个夜晚，
/// 唤醒卖花女孩」。
/// </para>
/// <para>
/// 平台口径（R-0037）：读当天白天账里的**投票动作**，按动作发生时的**角色快照**判定——
/// 任一次「投赞成」的举手者当时是恶魔 ⇒ 是；撤回不撤销已发生的举手。平台只推演，
/// 信息由说书人给出（D-0002）；涡流在场时这条信息必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class FlowergirlNightAction : INightAction, IAbilityResolution
{
    /// <summary>卖花女孩信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("flowergirl");

    private static readonly CharacterId Flowergirl = new("flowergirl");

    /// <inheritdoc />
    public CharacterId Character => Flowergirl;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reading = RetrospectiveReadings.DemonVoted(context.LastDay);

        return new ChoicePrompt
        {
            Context = "卖花女孩获得信息：说书人告诉『恶魔今天是否参与了投票』"
                + "（百科《卖花女孩》· 2026-10-01 抓取 · 角色简介；无论被提名者是否被处决都算）。"
                + $"按白天账推演：{RetrospectiveReadings.Describe(reading)}"
                + (reading is null ? "——记录读不出来，请说书人按现场判断。" : "。")
                + (VortoxInterference.IsActiveFor(context.State, context.Actor)
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
            throw new InvalidOperationException("卖花女孩的信息必须由说书人给出，不能是空的");
        }

        var reading = RetrospectiveReadings.DemonVoted(context.LastDay);
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
                        ? $"平台按白天账推演：{RetrospectiveReadings.Describe(reading)}"
                            + "（R-0037：按投票动作的角色快照；撤回不撤销举手）；信息由说书人给出"
                        : context.Outcome.Note),
            },
        ];
    }
}
