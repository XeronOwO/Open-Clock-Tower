using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 数学家的夜间行动：说书人给出「上一个黎明到此刻有多少名玩家的能力未正常生效」。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《数学家》· 2026-10-01 抓取 · 角色能力「每个夜晚，你会得知有多少名玩家的能力因为
/// 其他角色的能力而未正常生效。（从上个黎明到你被唤醒时）」；角色简介「数学家不会得知异常发生在
/// 哪名玩家身上，只会得知异常在多少名玩家身上发生了」「数学家的能力不会检测数学家自身是否
/// 未正常生效」；运作方式「唤醒数学家。对他用手势比划数字（0，1，2 等）……让数学家重新入睡」。
/// </para>
/// <para>
/// 平台口径（R-0004）：数字 = 窗口内**出现过计入分类的失效的玩家数**（按玩家去重、不含数学家本人）；
/// 窗口 = 失效账本的「上一个黎明之后」（首夜 = 开局起，见 R-0004 补充）。数字**由说书人给出**——
/// 本契约产出说书人裁定点，提示里带上平台按账本推演的值；平台只记录与推演，不判信息真假
/// （D-0002）。涡流在场时信息必须为假（R-0028）：提示与结果都加注。
/// </para>
/// </remarks>
internal sealed class MathematicianNightAction : INightAction, IAbilityResolution
{
    /// <summary>数学家信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("mathematician");

    private static readonly CharacterId Mathematician = new("mathematician");

    /// <inheritdoc />
    public CharacterId Character => Mathematician;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var count = CountFor(context.State, context.Actor);

        return new ChoicePrompt
        {
            Context = "数学家获得信息：说书人给出「上一个黎明到此刻有多少名玩家的能力未正常生效」"
                + "（按玩家去重，不含数学家本人；百科《数学家》· 2026-10-01 抓取 · 角色能力 / 角色简介）。"
                + $"按失效账本推演：{count}。"
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
            throw new InvalidOperationException("数学家的数字必须由说书人给出，不能是空的");
        }

        var count = CountFor(context.State, context.Actor);
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
                        ? $"平台按失效账本推演：{count}"
                            + "（R-0004：上个黎明起、按玩家去重、不含数学家本人）；数字由说书人给出"
                        : context.Outcome.Note),
            },
        ];
    }

    /// <summary>R-0004 的推演值：窗口内计入分类的席位去重后，排除数学家自己的席位。</summary>
    private static int CountFor(GameState state, SeatId actor) =>
        state.Malfunctions.CountedSeatsSinceDawn.Count(seat => seat != actor);
}
