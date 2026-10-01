namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求的生命周期状态。
/// </summary>
public enum OperationRequestStatus
{
    /// <summary>等待响应：一直有效，直到被响应或被上游变化作废（没有超时）。</summary>
    Pending,

    /// <summary>已被响应（玩家或说书人代填）。</summary>
    Answered,

    /// <summary>已作废（强制作废或依赖失效）。</summary>
    Voided,
}
