using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人强制作废当前挂起请求（无超时的对冲手段）。</summary>
public sealed record VoidRequestCommand : GameCommand
{
    /// <summary>被作废的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>作废原因。</summary>
    public required OperationRequestVoidReason Reason { get; init; }

    /// <summary>说明（进入审计与事件）。</summary>
    public string? Note { get; init; }
}
