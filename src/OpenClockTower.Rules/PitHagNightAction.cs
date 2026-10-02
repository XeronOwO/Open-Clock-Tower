using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 麻脸巫婆的夜间行动：选择一名玩家与一个角色；该角色不在场时他变成该角色。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《麻脸巫婆》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式——
/// 「每个夜晚*，你要选择一名玩家和一个角色，如果该角色不在场，他变成该角色。
/// 如果因此创造了一个恶魔，当晚的死亡由说书人决定」；「她无法创造重复角色，如果那个角色已在场，则无事发生」；
/// 「如果她选择的角色不在场，则唤醒所选玩家……用新角色标记替换他的旧角色标记」。
/// </para>
/// <para>
/// **两维原子选择**（同洗脑师，R-0021 的原语）：答案编码为 <c>{席位}|{角色 slug}</c>，不可只完成一维。
/// 目标集合 = 全体席位（含自己与已死亡玩家）——能力没写"不能"选谁，依《重要细节》三-1；
/// 百科《诺-达鲺》范例里麻脸巫婆把**已死亡**的诺-达鲺变成了卖花女孩。
/// </para>
/// <para>
/// 角色变更只写**角色维度**：阵营不变（百科《重要细节》二：「麻脸巫婆将善良的杂耍艺人变成了女巫，
/// 这位女巫仍然属于善良阵营」；六维度相互独立，架构 §2.1）。
/// </para>
/// <para>
/// 「创造恶魔 → 当晚死亡由说书人决定」不在本契约里：它需要跨恶魔段的死亡裁量窗口，
/// 见 <c>docs/standard/rulings.md</c> R-0030 与本票据的剩余部分。
/// </para>
/// </remarks>
internal sealed class PitHagNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => PitHagAbility.PitHag;

    /// <inheritdoc />
    public AbilityId Ability => PitHagAbility.TransformAbility;

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

        var characters = PitHagAbility.SelectableCharacters()
            .Select(character => new DecisionOption
            {
                Value = character.Value,
                Preview = PitHagAbility.DisplayNameOf(character),
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "麻脸巫婆选择一名玩家和一个角色：该角色不在场时他变成该角色（阵营不变）；"
                + "如果该角色已在场则无事发生（可选自己与已死亡玩家，依《重要细节》三-1）",
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
            // 中毒 / 醉酒 / 死亡：能力不生效——玩家照常选完目标，但什么都不发生。
            return [];
        }

        var (target, character) = ParseChoice(context.Choice);

        // 「如果她选择的角色在场，无事发生」——能力照常记「已使用且生效」，只是没有任何状态变化；
        // 这与"能力未生效"是两回事，不写失效账本（R-0004 的口径里没有这一项）。
        if (IsInPlay(context.State, character))
        {
            return [];
        }

        var events = new List<GameEvent>
        {
            new SeatStateChangedEvent
            {
                // 只写角色维度：阵营保持不变（六维度独立）。
                Seat = target,
                Character = character,
                PreviousCharacter = context.State.Seat(target)?.CharacterValue,
                Reason = $"麻脸巫婆角色变更：{PitHagAbility.DisplayNameOf(character)}",
                CausedBy = context.Actor,
            },
        };

        // 新角色今夜还有位置，就把那一格激活（百科《夜晚行动顺序一览》麻脸巫婆条：
        // 「一旦说书人决定杀死某个还未唤醒的恶魔，这名恶魔玩家当晚就不会被唤醒。否则，就需要唤醒这名玩家」）。
        // 契约在**结算时**调用：时机早于槽位推进，因此紧邻其后的槽位（原本口径下 麻脸巫婆 → 方古）也能激活。
        if (NightSlotActivation.Plan(
                context.Plan,
                context.SlotIndex,
                target,
                character,
                context.State,
                context.Seats,
                NightActions.Default) is { } activation)
        {
            events.Add(activation);
        }

        return events;
    }

    /// <summary>该角色是否已经在场（含已死亡玩家持有的角色——死亡玩家的角色标记仍在魔典上）。</summary>
    private static bool IsInPlay(GameState state, CharacterId character) =>
        state.Seats.Any(entry => entry.CharacterValue == character);

    /// <summary>解析两维答案（<c>seat:3|clockmaker</c>）；形状不对或角色不在表上一律显式抛错。</summary>
    private static (SeatId Target, CharacterId Character) ParseChoice(string? choice)
    {
        if (!ChoicePrompt.TrySplitAnswer(choice, out var primary, out var secondary) || secondary.Length == 0)
        {
            throw new InvalidOperationException($"麻脸巫婆的结算选择不是两维编码：{choice}");
        }

        var target = SeatChoice.Parse(primary)
            ?? throw new InvalidOperationException($"麻脸巫婆的结算缺少合法目标席位：{choice}");
        var character = new CharacterId(secondary);
        if (!PitHagAbility.SelectableCharacters().Contains(character))
        {
            throw new InvalidOperationException($"麻脸巫婆选择的角色不在角色表上：{character.Value}");
        }

        return (target, character);
    }
}
