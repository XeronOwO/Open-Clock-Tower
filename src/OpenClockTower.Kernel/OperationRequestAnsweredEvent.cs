namespace OpenClockTower.Kernel;

/// <summary>操作请求被响应（玩家本人或说书人代填）。</summary>
public sealed record OperationRequestAnsweredEvent : GameEvent
{
    /// <summary>被响应的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>响应内容与来源。</summary>
    public required OperationRequestAnswer Answer { get; init; }
}
