namespace OpenClockTower.Kernel;

/// <summary>
/// 一次「杂耍艺人公开猜测」的账目：谁在第几个白天公开猜了哪几条（R-0057-B）。
/// </summary>
/// <remarks>
/// <para>
/// 猜测是**公开事实**（百科《杂耍艺人》· 2026-10-01 抓取 · 角色简介 2：「必须公开（所有玩家听到）」），
/// 因此它留在白天账里、随白天公开面下发；**猜对数**只到本人（当晚由说书人给出，见 R-0057-B 第 2 条）。
/// </para>
/// <para>
/// 本记录只记事实，不算猜对数：判定要读结算时刻的角色快照，属于规则层的计算（规则层实现见
/// <c>docs/standard/rulings.md</c> R-0057-B）。
/// </para>
/// </remarks>
public sealed record JugglerGuessRecord
{
    /// <summary>猜测者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>这次猜测发生在第几个白天（1 = 首个白天）。</summary>
    public required int DayNumber { get; init; }

    /// <summary>这一批猜测，按玩家提交的顺序（0–5 条；0 条 = 公开声明但不猜）。</summary>
    public required IReadOnlyList<JugglerGuess> Guesses { get; init; }
}
