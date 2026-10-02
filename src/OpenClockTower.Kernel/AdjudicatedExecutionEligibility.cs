namespace OpenClockTower.Kernel;

/// <summary>
/// 规则层对「以某个来源处罚处决某席位」的判定结论（内核不认角色 slug，判定由契约给出）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：依据不成立（要求不存在 / 来源不生效 / 角色不匹配）
/// 一律显式拒绝、不产出任何事件；<see cref="Applicable"/> 为 null 表示事实没观测齐、**判定不了**，
/// 内核同样拒绝（D-0015：不猜）。
/// </para>
/// <para>
/// <see cref="DeathReason"/> 与 <see cref="CausedBy"/> 在判定成立时提供，用于产出死亡事实的归因。
/// </para>
/// </remarks>
public sealed record AdjudicatedExecutionEligibility
{
    /// <summary>true = 成立；false = 不成立；null = 事实不全，判定不了（拒绝，不猜）。</summary>
    public required bool? Applicable { get; init; }

    /// <summary>人可读的说明：不成立 / 判定不了时给出原因，成立时描述依据。</summary>
    public required string Note { get; init; }

    /// <summary>目标存活时死亡事实的原因（机器可读前缀 + 人可读说明）。</summary>
    public string? DeathReason { get; init; }

    /// <summary>死亡事实的导致方席位（如洗脑师席位）；没有特定归因时为 null。</summary>
    public SeatId? CausedBy { get; init; }

    /// <summary>死亡事实链接的要求 / 效果标识（归因链用）。</summary>
    public EffectId? EffectId { get; init; }
}
