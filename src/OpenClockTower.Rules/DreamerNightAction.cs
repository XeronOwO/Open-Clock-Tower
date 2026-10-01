using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 筑梦师的夜间行动：每夜选择一名其他玩家；结算时由说书人给出「一善一恶」两枚角色标记。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《筑梦师》· 2026-10-01 抓取 · 角色简介（该页无「规则细节」小节，按
/// <c>rulings.md</c> R-0011 标 L4 参考）：「每个夜晚，筑梦师需要选择一名其他玩家，
/// 并得知一个善良角色与一个邪恶角色，其中一个是那名玩家的真实角色」；「不能选择自己和旅行者作为目标」；
/// 「筑梦师得知的错误角色标记取决于所选玩家的真正的角色类型。如果筑梦师选择的玩家是镇民或外来者，
/// 对应的错误的邪恶角色可以是爪牙或恶魔……」。
/// </para>
/// <para>
/// 首版不含旅行者（R-0007 未决），目标集合 = 本局席位 − 自己。信息内容**不由引擎判定**（D-0002）：
/// 有效时由说书人从「对侧类型」的合法候选里选一枚错误标记，引擎只把「真角色 + 说书人选的那枚」
/// 如实记成一对；能力未生效时给自由文本，并标「可能错误」——该标记只进说书人视角，不下发玩家
/// （百科《重要细节》三-1：不要告诉玩家他醉酒或中毒）。
/// </para>
/// </remarks>
internal sealed class DreamerNightAction : INightAction, IAbilityResolution
{
    /// <summary>筑梦师信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("dreamer");

    private static readonly CharacterId Dreamer = new("dreamer");

    /// <inheritdoc />
    public CharacterId Character => Dreamer;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Seats
            .Where(seat => seat != context.Actor)
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "筑梦师选择一名其他玩家（不能选自己；首版没有旅行者）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"筑梦师的结算缺少合法目标席位：{context.Choice}");

        if (!context.Outcome.Effective)
        {
            return new ChoicePrompt
            {
                Context = $"筑梦师（{context.Actor.Value} 号）的能力未生效："
                    + "信息由你说书人裁定，可以是错的（百科《重要细节》三-3）。请填写要传达的内容。",
                Options = [],
                OnNoOption = NoOptionBehavior.StorytellerDecides,
            };
        }

        var character = context.State.Seat(target)?.CharacterValue
            ?? throw new InvalidOperationException(
                $"席位 {target.Value} 的角色尚未观测，列不出筑梦师的合法候选");
        var type = SectsAndVioletsRoster.TypeOf(character)
            ?? throw new InvalidOperationException(
                $"角色 {character.Value} 不在首版花名册里，列不出筑梦师的合法候选");

        var targetIsGood = IsGood(type);
        var candidates = targetIsGood ? EvilCharacters() : GoodCharacters();

        return new ChoicePrompt
        {
            Context = $"筑梦师看到 {target.Value} 号玩家的真实角色「{Display(character)}」；"
                + $"请再选一枚{(targetIsGood ? "邪恶" : "善良")}角色标记作为错误项"
                + "（百科《筑梦师》· 2026-10-01 抓取 · 角色简介）。",
            Options =
            [
                .. candidates.Select(candidate => new DecisionOption
                {
                    Value = candidate.Value,
                    Preview = Display(candidate),
                }),
            ],
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"筑梦师的结算缺少合法目标席位：{context.Choice}");

        return
        [
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = ComposeContent(context, target),
                MayBeFalse = true,
                Note = context.Outcome.Effective
                    ? "筑梦师的信息按其能力设定本来就是一真一假；平台不判定哪一枚为真（D-0002）"
                    : context.Outcome.Note,
            },
        ];
    }

    private static string ComposeContent(AbilityResolutionContext context, SeatId target)
    {
        if (!context.Outcome.Effective)
        {
            if (string.IsNullOrWhiteSpace(context.Decision))
            {
                throw new InvalidOperationException("筑梦师能力未生效时，说书人必须给出信息内容（可为假）");
            }

            return context.Decision;
        }

        var trueCharacter = context.State.Seat(target)?.CharacterValue
            ?? throw new InvalidOperationException(
                $"席位 {target.Value} 的角色尚未观测，组不成筑梦师的信息");
        var falseCharacter = ParseCandidate(context.Decision);

        var trueType = SectsAndVioletsRoster.TypeOf(trueCharacter)
            ?? throw new InvalidOperationException($"角色 {trueCharacter.Value} 不在首版花名册里");
        var falseType = SectsAndVioletsRoster.TypeOf(falseCharacter)
            ?? throw new InvalidOperationException($"角色 {falseCharacter.Value} 不在首版花名册里");
        if (IsGood(trueType) == IsGood(falseType))
        {
            throw new InvalidOperationException(
                $"筑梦师的错误项必须与真实角色分属善良 / 邪恶两侧：真实 {trueCharacter.Value}，"
                + $"裁定 {falseCharacter.Value}");
        }

        return $"{target.Value} 号玩家是「{Display(trueCharacter)}」或「{Display(falseCharacter)}」";
    }

    private static CharacterId ParseCandidate(string? decision) =>
        string.IsNullOrWhiteSpace(decision)
            ? throw new InvalidOperationException("筑梦师的裁定没有给出错误项角色")
            : new CharacterId(decision);

    private static IReadOnlyList<CharacterId> GoodCharacters() =>
    [
        .. SectsAndVioletsRoster.OfType(CharacterType.Townsfolk),
        .. SectsAndVioletsRoster.OfType(CharacterType.Outsider),
    ];

    private static IReadOnlyList<CharacterId> EvilCharacters() =>
    [
        .. SectsAndVioletsRoster.OfType(CharacterType.Minion),
        .. SectsAndVioletsRoster.OfType(CharacterType.Demon),
    ];

    private static bool IsGood(CharacterType type) =>
        type is CharacterType.Townsfolk or CharacterType.Outsider;

    private static string Display(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;
}
