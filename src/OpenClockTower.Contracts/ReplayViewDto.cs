namespace OpenClockTower.Contracts;

/// <summary>
/// 复盘视图（一页）：按事件序号排好序的步骤 + 数据边界。
/// </summary>
/// <remarks>
/// 终局后可见的玩家面契约（R-0043 / D-0020）：只在 <c>GameEndedEvent</c> 之后下发给对局内玩家；
/// 进行中任何玩家收包零复盘字段（服务端闸在 <c>GameSession.GetReplayAsync</c>）。
/// </remarks>
public sealed record ReplayViewDto
{
    /// <summary>最新事件序号（玩家面 = 结束批次序号；说书人实时面 = 当前最新）。</summary>
    public required long Sequence { get; init; }

    /// <summary>本局是否已经结束（<c>GameEndedEvent</c> 已出现）。</summary>
    public required bool Ended { get; init; }

    /// <summary>本页之后还有没有步骤（客户端据此继续懒加载）。</summary>
    public required bool HasMore { get; init; }

    /// <summary>本页步骤；按序号严格递增。</summary>
    public required ReplayStepDto[] Steps { get; init; }

    /// <summary>公开的「席位 → 玩家名」快照（D-0021）：圆盘标记等客户端渲染用同一份名字。</summary>
    public required SeatDisplayNameDto[] SeatNames { get; init; }
}
