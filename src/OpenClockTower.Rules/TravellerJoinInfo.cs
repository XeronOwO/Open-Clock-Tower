using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 旅行者加入时的私密信息面（票据 `traveller-and-exile` D1）：邪恶旅行者「得知恶魔是谁」的信息口径。
/// </summary>
/// <remarks>
/// 依据百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式第 2 步——让旅行者加入邪恶阵营时，
/// 「你需要让他得知恶魔是谁，但不告诉他哪些玩家是爪牙。（告诉他一名存活的恶魔玩家，
/// 或告诉他所有存活的邪恶恶魔玩家，根据当前游戏的实际情况来决定并给出最适合被该旅行者得知的信息。）」
/// 选一名还是全部由**说书人**决定；平台只校验与转达（D-0002：平台不替说书人拍板）。
/// </remarks>
public static class TravellerJoinInfo
{
    /// <summary>「邪恶旅行者得知恶魔」这条私密信息的能力标识：属加入流程，不是角色能力。</summary>
    public static readonly AbilityId EvilRevealAbility = new("traveller.evil-reveal");

    /// <summary>把说书人指定的存活恶魔席位拼成给邪恶旅行者的信息原文（按席位升序，顺序稳定）。</summary>
    public static string ComposeEvilReveal(IReadOnlyList<SeatId> demonSeats)
    {
        ArgumentNullException.ThrowIfNull(demonSeats);

        var ordered = demonSeats.OrderBy(seat => seat.Value).ToArray();
        return ordered.Length == 1
            ? $"{ordered[0].Value} 号玩家是恶魔。"
            : $"存活的恶魔玩家：{string.Join("、", ordered.Select(seat => $"{seat.Value} 号"))}。";
    }
}
