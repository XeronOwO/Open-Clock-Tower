namespace OpenClockTower.Contracts;

/// <summary>
/// 说书人的死亡保护裁定提示（R-0048）：只在这一席此刻真能被裁定时下发；玩家投影里没有它。
/// </summary>
/// <remarks>
/// 与 <see cref="DayProtectionDto"/>（当天已裁定的公开账目）分工：这是一条**待办入口**，
/// 只在「收票收完 + 达线 + 目标存活 + 未裁定 + 保护来源要求裁定（或观测不齐）」时存在。
/// </remarks>
public sealed record DayProtectionPromptDto
{
    /// <summary>待裁定的席位（当前开放流放的目标）。</summary>
    public required int Seat { get; init; }

    /// <summary>NeedsRuling（先说书人裁定）/ Indeterminate（先补观测）。</summary>
    public required string Outcome { get; init; }

    /// <summary>给说书人的说明（差什么、下一步做什么）。</summary>
    public required string Note { get; init; }
}
