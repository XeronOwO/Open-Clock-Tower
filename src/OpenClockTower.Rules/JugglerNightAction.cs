using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 杂耍艺人的夜间行动：说书人比划「猜对的数量」（0–5）——当晚的那条信息只到本人（R-0057-B）。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力——「在你的首个白天，你可以公开猜测任意玩家的角色
/// 最多五次。在当晚，你会得知猜测正确的角色数量。」；· 角色简介 3——「如果在醉酒或中毒时做出了猜测，
/// 但当晚他的能力触发时却清醒且健康，那么说书人仍然会给他真实的信息」（生效判定在**触发时刻**）；
/// · 运作方式 5——当晚唤醒杂耍艺人，用手势比划数量，随后移除提示标记。
/// </para>
/// <para>
/// 平台口径（R-0057-B）：猜对数按**结算时刻**的角色快照求值（判定与生效同一时刻，可重放、可解释），
/// 平台只给出**推演值**；数字仍由说书人给出（D-0002）。昨天没有做出猜测时不唤醒（空选项 + Skip）；
/// 涡流在场时信息必须为假（R-0028），提示与结果都加注。
/// </para>
/// </remarks>
internal sealed class JugglerNightAction : INightAction, IAbilityResolution
{
    /// <summary>杂耍艺人信息能力标识。</summary>
    public static readonly AbilityId InfoAbility = new("juggler");

    private static readonly CharacterId Juggler = new("juggler");

    /// <inheritdoc />
    public CharacterId Character => Juggler;

    /// <inheritdoc />
    public AbilityId Ability => InfoAbility;

    /// <inheritdoc />
    public IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) =>
        VortoxInterference.MalfunctionsFor(context);

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (GuessesOf(context.LastDay, context.Actor) is not { } guesses)
        {
            // 昨天的白天他没有做出公开猜测：本夜不唤醒。规则细节 2 的「对新人宽容处理」
            // 是说书人自己的裁量（不在平台路径上）；这里按能力原文收口（空选项 + Skip，R-0009）。
            return new ChoicePrompt
            {
                Context = "杂耍艺人昨天的白天没有做出公开猜测：本夜不给信息"
                    + "（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力；R-0057-B）",
                Options = [],
                OnNoOption = NoOptionBehavior.Skip,
            };
        }

        return new ChoicePrompt
        {
            Context = "杂耍艺人获得信息：说书人比划他猜对的**数量**（0–5）"
                + "（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力 / 运作方式 5）。"
                + CountText(context.State, guesses)
                + (VortoxInterference.IsActiveFor(context.State, context.Actor)
                    ? "涡流在场：这条信息必须为假（R-0028）——给出与推演不同的数量。"
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
            throw new InvalidOperationException("杂耍艺人的数量必须由说书人给出，不能是空的");
        }

        var count = GuessesOf(context.LastDay, context.Actor) is { } guesses
            ? CountText(context.State, guesses)
            : "他昨天的白天没有做出公开猜测（R-0057-B）：本夜的信息由说书人裁量";

        // 与数学家的口径同族：未生效时说明以失效原因为主，推演值附在后面（审计两头都看得到）。
        var note = context.Outcome.Effective
            ? count
            : $"{context.Outcome.Note ?? "能力未生效"}（{count}）";

        return
        [
            new InformationResultIssuedEvent
            {
                Recipient = context.Actor,
                Ability = Ability,
                Content = context.Decision,
                MayBeFalse = !context.Outcome.Effective || VortoxInterference.IsActiveFor(context.State, context.Actor),
                Note = VortoxInterference.NoteFor(context.State, context.Actor, note),
            },
        ];
    }

    /// <summary>最近一个白天里这个席位的公开猜测；没有记录时返回 null（= 本夜不唤醒）。</summary>
    private static IReadOnlyList<JugglerGuess>? GuessesOf(DayRecord? lastDay, SeatId actor) =>
        lastDay?.JugglerGuesses.LastOrDefault(record => record.Seat == actor)?.Guesses;

    /// <summary>
    /// 按结算时刻的角色快照推演猜对数；被猜席位的角色没观测齐时说明**判不了**（不猜，D-0015）。
    /// </summary>
    private static string CountText(GameState state, IReadOnlyList<JugglerGuess> guesses)
    {
        if (guesses.Any(guess => state.Seat(guess.Seat)?.CharacterValue is null))
        {
            return "账上还有被猜席位的角色没观测齐：现在算不出猜对数（说书人自行裁量）。";
        }

        var correct = guesses.Count(guess => state.Seat(guess.Seat)?.CharacterValue == guess.Character);
        return $"按结算时刻的角色快照推演：猜对 {correct} 条（共提交 {guesses.Count} 条）。";
    }
}
