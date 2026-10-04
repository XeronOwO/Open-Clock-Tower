namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人「现在该给这一席做死亡保护裁定」的提示（R-0048；说书人投影字段）。
/// </summary>
/// <remarks>
/// <see cref="Outcome"/> 只会是 <see cref="DeathProtectionOutcome.NeedsRuling"/>（先说书人裁定：
/// 有趣 → 本次流放不死亡）或 <see cref="DeathProtectionOutcome.Indeterminate"/>（先补维度观测）；
/// 其余情形由 <see cref="DayProtectionPromptQuery"/> 返回 null——平台不提前提问、不预缓存（R-0048 第 2 条）。
/// </remarks>
public sealed record DayProtectionPrompt
{
    /// <summary>待裁定的席位（当前开放流放的目标）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>提示状态：NeedsRuling / Indeterminate。</summary>
    public required DeathProtectionOutcome Outcome { get; init; }

    /// <summary>给说书人的说明（差什么、下一步做什么）。</summary>
    public required string Note { get; init; }
}
