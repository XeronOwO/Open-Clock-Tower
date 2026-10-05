using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 「首个白天」的起算（R-0057-B 第 3 条）：杂耍艺人只能在他**持有该角色之后**的第一个白天公开猜测。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力——「在你的**首个白天**，你可以公开猜测……在当晚，
/// 你会得知猜测正确的角色数量。」；· 范例 2——「第四个夜晚，博学者变成了杂耍艺人。**下个白天**，
/// 新的杂耍艺人猜测……当晚得知『1』」——「首个白天」是**该角色**的第一个白天，不是整局的第一天
/// （与《哲学家》提示 11 同义）。
/// </para>
/// <para>
/// 平台口径：起算日 = 账上**最近一次**「变成杂耍艺人」（角色变化）或「开局之后第一次观测到这个角色」
/// 的那个白天；变化发生在夜里 → 次日算起，发生在白天 → 当天算起；开局分配的角色 → 第 1 天。
/// 账上说不清（没有获得记录，且角色对不上）时返回 null，由调用方显式拒绝（不猜，D-0015）。
/// </para>
/// </remarks>
internal static class JugglerGuessWindow
{
    private static readonly CharacterId Juggler = new("juggler");

    /// <summary>该席位当前这次持有杂耍艺人的首个白天；判不了返回 null。</summary>
    internal static int? FirstHeldDay(GameState state, SeatId seat)
    {
        ArgumentNullException.ThrowIfNull(state);

        // 从最近往回找：这个席位最近一次「拿到杂耍艺人」的那一条。
        var entries = state.Activity.Entries;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (entry.Seat != seat
                || entry.Character != Juggler
                || entry.Kind is not (SeatActivityKind.CharacterChange or SeatActivityKind.CharacterObserved))
            {
                continue;
            }

            return entry.DuringNight ? entry.DayNumber + 1 : entry.DayNumber;
        }

        // 账上没有获得记录：只可能是开局分配——随后就是第 1 天；角色对不上则判不了。
        return state.Seat(seat)?.CharacterValue == Juggler ? 1 : null;
    }
}
