namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求作废的内容：原因 + 人类可读说明。
/// </summary>
/// <remarks>
/// "谁、为什么、当时状态"的完整审计由应用层记录；内核只记原因与说明，
/// 保持确定性与可重放。
/// </remarks>
public sealed record OperationRequestVoid
{
    /// <summary>作废原因。</summary>
    public required OperationRequestVoidReason Reason { get; init; }

    /// <summary>说明（例如哪条依赖不满足、说书人强推的理由）。</summary>
    public string? Note { get; init; }
}
