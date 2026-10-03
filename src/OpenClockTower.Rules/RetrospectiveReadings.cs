using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 回溯型信息能力（卖花女孩 / 城镇公告员 / 神谕者）的**推演读数**：按白天账与状态账算出
/// 「按记录应该是多少」。
/// </summary>
/// <remarks>
/// <para>
/// 推演只是说书人裁定点的提示，最终信息由说书人给出（D-0002）；口径见
/// <c>docs/standard/rulings.md</c> R-0037：**举手动作 = 投过票**（撤回不撤销），
/// 恶魔 / 爪牙的身份按**动作发生时的角色快照**认，不看夜晚时刻的当前角色。
/// </para>
/// <para>
/// 一律**不猜**：读数返回 null 表示「记录读不出来」（还没有白天 / 缺角色快照 / 维度未观测），
/// 提示里如实写明，绝不把"不知道"折成"否"或 0。
/// </para>
/// </remarks>
internal static class RetrospectiveReadings
{
    /// <summary>恶魔今天投过赞成吗？null = 读不出来。</summary>
    public static bool? DemonVoted(DayRecord? day)
    {
        if (day is null)
        {
            return null;
        }

        var votes = day.VoteAttempts.Where(attempt => attempt.Voted).ToArray();
        if (votes.Length == 0)
        {
            // 一次举手都没有：无论快照齐不齐，"否"都是确定的。
            return false;
        }

        // 先找确定项：任何一次举手当时是恶魔 ⇒ 就是"是"，与其余记录无关。
        if (votes.Any(IsDemonVote))
        {
            return true;
        }

        // 确定项没有，但还有快照缺失的举手——它可能是恶魔，"不能猜"。
        return votes.All(attempt => attempt.VoterCharacter is not null) ? false : null;
    }

    /// <summary>爪牙今天发起过提名吗？null = 读不出来。</summary>
    public static bool? MinionNominated(DayRecord? day)
    {
        if (day is null)
        {
            return null;
        }

        if (day.Nominations.Count == 0)
        {
            return false;
        }

        if (day.Nominations.Any(IsMinionNomination))
        {
            return true;
        }

        return day.Nominations.All(item => item.NominatorCharacter is not null) ? false : null;
    }

    /// <summary>当前账里「死亡且邪恶」的席位数；null = 有席位的生死 / 阵营未观测。</summary>
    public static int? DeadEvilCount(GameState state, IReadOnlyList<SeatId> seats)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);

        var count = 0;
        foreach (var seat in seats)
        {
            var entry = state.Seat(seat);
            if (entry?.LifeValue is not { } life || entry.Alignment?.Value is not { } alignment)
            {
                return null;
            }

            if (life == LifeState.Dead && alignment == Alignment.Evil)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>把读数写成提示里的字面：是 / 否 / 数字 / 无法判定。</summary>
    public static string Describe(int? reading) =>
        reading is { } value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "无法判定";

    /// <summary>把读数写成提示里的字面：是 / 否 / 无法判定。</summary>
    public static string Describe(bool? reading) => reading switch
    {
        true => "是",
        false => "否",
        _ => "无法判定",
    };

    private static bool IsDemonVote(DayVoteAttempt attempt) =>
        attempt.Voted
        && attempt.VoterCharacter is { } character
        && SectsAndVioletsRoster.TypeOf(character) == CharacterType.Demon;

    private static bool IsMinionNomination(NominationRecord nomination) =>
        nomination.NominatorCharacter is { } character
        && SectsAndVioletsRoster.TypeOf(character) == CharacterType.Minion;
}
