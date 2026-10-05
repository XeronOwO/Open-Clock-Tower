namespace OpenClockTower.Kernel;

/// <summary>
/// 杂耍艺人的白天公开猜测（R-0057-B）：玩家命令 → 校验（首个白天 / 未用过 / 形状）→
/// 公开事实进白天账（所有玩家可见）。
/// </summary>
/// <remarks>
/// <para>
/// 与艺术家 / 博学者两族的差别：这条能力**不需要说书人裁定**——猜测是玩家自己说出来的话，
/// 平台只做记录与公开；当晚的信息才是说书人给的（<c>JugglerNightAction</c>，规则层）。
/// </para>
/// <para>
/// 三条硬口径（R-0057-B）：只在**该角色被持有后的首个白天**能猜（角色变更后重新起算）；
/// 每个首个白天只有**一次**公开猜测（0–5 条，一次提交）；猜测**公开**、猜对数只到本人。
/// 「重获能力」窗口把这一次持有重新起算（集骨者，R-0054 第 4 条；与艺术家提问同款放宽）。
/// </para>
/// </remarks>
internal static class JugglerGuessMachine
{
    /// <summary>单次猜测的条数上限（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力）。</summary>
    internal const int MaxGuesses = 5;

    /// <summary>处理一条「杂耍艺人公开猜测」输入。</summary>
    internal static DayOutcome Make(DayState state, SettlementContext context, MakeJugglerGuessesInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var day = state.OpenDay;
        if (day is null)
        {
            return DayOutcome.Reject(
                "juggler.not_open_day",
                "现在是夜晚，或白天已经结束：杂耍艺人只能在自己的首个白天公开猜测（R-0057-B）");
        }

        if (input.Guesses.Count > MaxGuesses)
        {
            return DayOutcome.Reject(
                "juggler.too_many_guesses",
                $"杂耍艺人最多猜 {MaxGuesses} 条（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力）："
                + $"这次提交了 {input.Guesses.Count} 条");
        }

        var character = context.State.Seat(input.Seat)?.CharacterValue;
        if (character is not { } seatCharacter)
        {
            return DayOutcome.Reject(
                "juggler.character_unobserved",
                $"{input.Seat.Value} 号的角色还没有观测：无法确认是不是杂耍艺人（不猜，D-0015）");
        }

        var source = context.JugglerGuesses.FirstOrDefault(candidate => candidate.Character == seatCharacter);
        if (source is null)
        {
            return DayOutcome.Reject(
                "juggler.not_juggler",
                $"{input.Seat.Value} 号（{seatCharacter.Value}）不是可以公开猜测的杂耍艺人");
        }

        if (source.FirstHeldDay(context.State, input.Seat) is not { } firstDay)
        {
            return DayOutcome.Reject(
                "juggler.tenure_unknown",
                "账上说不清这个席位是什么时候拿到杂耍艺人的（没有角色变化 / 首次观测记录）："
                + "先补齐观测与变化记录，再让他猜（不猜，D-0015）");
        }

        // 集骨者「重获能力」窗口（R-0054 第 4 条）：重获 = 这一次持有重新起算——死亡但重获能力的杂耍艺人
        // 即使先前已经猜过，也能在窗口存续的这个白天再猜一次（与艺术家提问的同一处放宽，
        // 见 ArtistQuestionMachine.Ask；窗口到期（下个黄昏）后回到原口径）。
        var regained = context.State.RegainedAbilityOn(input.Seat) == true;
        var tenureStart = regained ? day.DayNumber : firstDay;

        if (tenureStart != day.DayNumber)
        {
            return DayOutcome.Reject(
                "juggler.not_first_day",
                $"杂耍艺人只能在自己持有该角色后的**首个白天**猜测：他这次持有的首个白天是第 {firstDay} 天，"
                + $"现在是第 {day.DayNumber} 天（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力；R-0057-B 第 3 条）");
        }

        // 「用过一次」按**这次持有**记账：首个白天当天或之后出现过的猜测记录都算用过
        // （首个白天只有一次公开猜测，见 R-0057-B）；重获窗口把起算点挪到今天，因此**窗口内那次**
        // 用的是这一次机会，重获之前猜过也不影响——但同一天仍然只有一次（窗口终止后回到原起算点）。
        if (state.Days.Any(record => record.JugglerGuesses.Any(guess =>
                guess.Seat == input.Seat && guess.DayNumber >= tenureStart)))
        {
            return DayOutcome.Reject(
                "juggler.already_guessed",
                $"{input.Seat.Value} 号这次持有杂耍艺人已经公开猜过了：首个白天只有一次公开猜测"
                + "（百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力；R-0057-B）");
        }

        foreach (var guess in input.Guesses)
        {
            if (!context.Seats.Contains(guess.Seat) || context.State.HasDeparted(guess.Seat))
            {
                return DayOutcome.Reject(
                    "juggler.guess_unknown_seat",
                    $"{guess.Seat.Value} 号不在本局座次里（或已经离场）：只能猜在局的玩家");
            }

            if (!source.IsKnownCharacter(guess.Character))
            {
                return DayOutcome.Reject(
                    "juggler.guess_unknown_character",
                    $"「{guess.Character.Value}」不在本剧本的角色表里：猜的角色必须是剧本内的角色");
            }
        }

        return DayOutcome.Accepted(
        [
            new JugglerGuessesMadeEvent
            {
                Seat = input.Seat,
                DayNumber = day.DayNumber,
                Guesses = [.. input.Guesses],
            },
        ]);
    }
}
