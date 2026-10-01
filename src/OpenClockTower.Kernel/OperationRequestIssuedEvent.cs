namespace OpenClockTower.Kernel;

/// <summary>一次操作请求被发出（服务端将定向投递给被请求的玩家）。</summary>
public sealed record OperationRequestIssuedEvent : GameEvent
{
    /// <summary>请求全文（含选项与依赖）；落库后即为断线重连时原样重投递的快照。</summary>
    public required OperationRequest Request { get; init; }
}
