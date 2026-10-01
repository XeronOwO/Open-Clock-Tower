namespace OpenClockTower.Kernel;

/// <summary>操作请求被作废（强制作废或依赖失效）。</summary>
public sealed record OperationRequestVoidedEvent : GameEvent
{
    /// <summary>被作废的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>作废原因与说明。</summary>
    public required OperationRequestVoid Void { get; init; }
}
