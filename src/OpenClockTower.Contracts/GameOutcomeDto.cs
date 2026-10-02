namespace OpenClockTower.Contracts;

/// <summary>
/// 胜负结论：胜方 + 条件分类 + 说明。
/// </summary>
/// <remarks>
/// 对局结束后对全体玩家一致可见（R-0024）：结束时不再存在信息隔离的理由；
/// 原因是"哪条条件成立"的公开说明，不含任何未公开的隐藏状态。
/// </remarks>
public sealed record GameOutcomeDto
{
    /// <summary>
    /// 这份结论被表达时的序号：推送 = 背书事件（GameEndedEvent）序号；快照 = 快照序号。
    /// 客户端按它做字段级取舍（同请求 / 白天投影的既有口径）。
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>获胜阵营：Good / Evil。</summary>
    public required string Winner { get; init; }

    /// <summary>条件分类（OutcomeCondition 的枚举名）。</summary>
    public required string Condition { get; init; }

    /// <summary>人类可读说明（结束横幅用）。</summary>
    public required string Detail { get; init; }
}
