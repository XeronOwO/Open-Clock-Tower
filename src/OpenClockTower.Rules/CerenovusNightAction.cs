using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 洗脑师的夜间行动：选择一名玩家与一个善良角色，形成「明天白天与夜晚疯狂证明是该角色」的要求。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《洗脑师》· 2026-10-01 抓取 · 角色能力 / 运作方式：
/// 「每个夜晚，唤醒洗脑师；让洗脑师指向一名玩家和角色列表上一个镇民或外来者图标」；
/// 「如果你在白天的常规处决前处决了他，则直接进入夜晚阶段」。
/// </para>
/// <para>
/// **两维原子选择**（R-0021）：以单个操作请求承载（<see cref="ChoicePrompt.SecondaryOptions"/>），
/// 答案编码为 <c>{席位}|{角色 slug}</c>；不可只完成一维。
/// </para>
/// <para>
/// 目标集合 = 全体席位：能力没有写"不能"选谁，且百科《重要细节》· 2026-10-01 抓取 · 三-1 规定
/// 「在夜晚选择『任意玩家』，意思就是可以选择自己或是选择已死亡的玩家」；洗脑师提示与技巧也明说
/// 可以影响已死亡的玩家。
/// </para>
/// <para>
/// 行动当时醉酒 / 中毒 → 不写入要求、不告知目标（《洗脑师》提示标记：「若此时洗脑师醉酒中毒，
/// 不放置该标记」）；结算契约只负责写事实，是否疯狂由说书人裁定（R-0003）。
/// </para>
/// </remarks>
internal sealed class CerenovusNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => CerenovusAbility.Cerenovus;

    /// <inheritdoc />
    public AbilityId Ability => CerenovusAbility.ActionAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seats = context.Seats
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        var characters = CerenovusAbility.SelectableCharacters()
            .Select(character => new DecisionOption
            {
                Value = character.Value,
                Preview = CerenovusAbility.DisplayNameOf(character),
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "洗脑师选择一名玩家和一个善良角色（镇民或外来者）：他明天白天与夜晚需要「疯狂」地"
                + "证明自己是这个角色，否则说书人可能处决他（可选自己与已死亡玩家，依《重要细节》三-1）",
            Options = seats,
            SecondaryOptions = characters,
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
            // 中毒 / 醉酒 / 死亡：能力不生效——按提示标记的放置条件，此时**不放置**标记、
            // 也不给目标任何信息（百科《洗脑师》· 提示标记「若此时洗脑师醉酒中毒，不放置该标记」）。
            return [];
        }

        var (target, character) = ParseChoice(context.Choice);
        var displayName = CerenovusAbility.DisplayNameOf(character);

        return
        [
            new MadnessRequirementIssuedEvent
            {
                Requirement = new MadnessRequirement
                {
                    Id = CerenovusAbility.RequirementId(context.SlotKey),
                    Seat = target,
                    ProveToBe = displayName,

                    // 施加时来源的角色取槽位记录的施加时角色（过时不候，同 AbilityResolutionContext 的口径）。
                    Source = context.Actor,
                    SourceCharacter = context.ActorCharacter,
                    Ability = CerenovusAbility.MadnessAbility,
                    ExpiresAtDay = CerenovusAbility.ExpiresAtDay(context.DaysStarted),
                },
            },
            new InformationResultIssuedEvent
            {
                Recipient = target,
                Ability = CerenovusAbility.MadnessAbility,
                Content = $"洗脑师对你使用了能力：明天白天与夜晚，你要努力让其他人相信你是「{displayName}」；"
                    + "若说书人认为你没有尽力，你可能被处决（说书人对你是否疯狂有最终裁决权）。",
                MayBeFalse = false,
                Note = "按《洗脑师》运作方式把「该角色的能力对你生效 + 要证明的角色」告知目标；"
                    + "引擎不判定目标是否疯狂（R-0003）",
            },
        ];
    }

    /// <summary>解析两维答案（<c>seat:3|clockmaker</c>）；形状不对或角色不在可选集合里一律显式抛错。</summary>
    private static (SeatId Target, CharacterId Character) ParseChoice(string? choice)
    {
        // 拆分规则与内核共用一份（ChoicePrompt.TrySplitAnswer）：编码改了不会两边漂移。
        if (!ChoicePrompt.TrySplitAnswer(choice, out var primary, out var secondary) || secondary.Length == 0)
        {
            throw new InvalidOperationException($"洗脑师的结算选择不是两维编码：{choice}");
        }

        var target = SeatChoice.Parse(primary)
            ?? throw new InvalidOperationException($"洗脑师的结算缺少合法目标席位：{choice}");
        var character = new CharacterId(secondary);
        if (!CerenovusAbility.SelectableCharacters().Contains(character))
        {
            throw new InvalidOperationException(
                $"洗脑师选择的目标角色不在「镇民或外来者」集合里：{character.Value}");
        }

        return (target, character);
    }
}
