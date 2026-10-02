using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>最近一次被作废的操作请求（说书人视图用：请求为什么没了）。</summary>
public sealed record VoidedRequestSnapshot
{
    /// <summary>被作废的请求标识。</summary>
    public required OperationRequestId Id { get; init; }

    /// <summary>作废原因分类。</summary>
    public required OperationRequestVoidReason Reason { get; init; }

    /// <summary>说明；依赖失效时写明是哪一条依赖不满足（Kernel SeatDependencyCheck.Describe）。</summary>
    public string? Note { get; init; }

    /// <summary>产生这条作废的事件序号。</summary>
    public required long Sequence { get; init; }
}
