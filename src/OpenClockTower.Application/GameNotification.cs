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

    /// <summary>
    /// 背书事件的事件流序号：客户端据此与快照序号比较先后、按序号合并
    /// （票据 player-information-resync-race；此前推送无序号，补齐响应会覆盖窗口内到达的推送）。
    /// </summary>
    /// <remarks>
    /// DayChanged 取本批最后一条白天事件的序号；分发器对"读时状态"型推送（白天投影、说书人视图）
    /// 以读取到的视图序号为准，那才是该状态被表达时的序号。
    /// </remarks>
    public required long Sequence { get; init; }

    /// <summary>目标玩家席位；面向说书人或全体玩家（阶段开始）的通知为 null。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>操作请求（Issued 时非空）。</summary>
    public OperationRequest? Request { get; init; }

    /// <summary>被作废 / 被响应的请求标识（Voided / Answered 时非空）。</summary>
    public OperationRequestId? RequestId { get; init; }

    /// <summary>作废内容（Voided 时非空）。</summary>
    public OperationRequestVoid? Void { get; init; }

    /// <summary>响应内容与来源（Answered 时非空）。</summary>
    public OperationRequestAnswer? Answer { get; init; }

    /// <summary>新阶段（PhaseStarted 时非空；面向全体玩家广播）。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>信息类结果（InformationResultIssued 时非空）。</summary>
    public InformationResultIssuedEvent? Information { get; init; }
}
