using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 提交完成后要发出的通知：服务端据此推送。
/// </summary>
/// <remarks>
/// 推送在事件提交**之后**发出；推丢不致命——重连时按序号补齐并重投未响应的请求（D-0010 / D-0011）。
/// </remarks>
public sealed record GameNotification
{
    /// <summary>通知类别。</summary>
    public required GameNotificationKind Kind { get; init; }

    /// <summary>目标玩家席位；面向说书人的通知为 null。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>操作请求（Issued 时非空）。</summary>
    public OperationRequest? Request { get; init; }

    /// <summary>被作废的请求标识（Voided 时非空）。</summary>
    public OperationRequestId? RequestId { get; init; }

    /// <summary>作废内容（Voided 时非空）。</summary>
    public OperationRequestVoid? Void { get; init; }

    /// <summary>信息类结果（InformationResultIssued 时非空）。</summary>
    public InformationResultIssuedEvent? Information { get; init; }
}
