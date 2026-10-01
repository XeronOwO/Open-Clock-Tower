namespace OpenClockTower.Kernel;

/// <summary>强制作废当前挂起请求（D-0011 无超时的对冲手段之一）。</summary>
public sealed record VoidRequestInput : StepMachineInput
{
    /// <summary>被作废的请求（必须是当前挂起的那个）。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>作废原因。</summary>
    public required OperationRequestVoidReason Reason { get; init; }

    /// <summary>说明（谁、为什么——完整审计由应用层记录）。</summary>
    public string? Note { get; init; }
}
