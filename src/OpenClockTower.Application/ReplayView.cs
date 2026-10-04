namespace OpenClockTower.Application;

/// <summary>
/// 复盘视图：一页按事件序号排好序的步骤 + 数据边界。
/// </summary>
/// <remarks>
/// 只读投影：服务端按需从持久化事件流重建（R-0043 第 4 条），不依赖宿主内存态。
/// </remarks>
public sealed record ReplayView
{
    /// <summary>最新事件序号（玩家面 = 结束批次序号；说书人实时面 = 当前最新）。</summary>
    public required long Sequence { get; init; }

    /// <summary>本局是否已经结束（<c>GameEndedEvent</c> 已出现）。</summary>
    public required bool Ended { get; init; }

    /// <summary>本页之后还有没有步骤（客户端据此继续懒加载）。</summary>
    public required bool HasMore { get; init; }

    /// <summary>本页步骤；按 <see cref="ReplayStep.Sequence"/> 严格递增。</summary>
    public required IReadOnlyList<ReplayStep> Steps { get; init; }
}
