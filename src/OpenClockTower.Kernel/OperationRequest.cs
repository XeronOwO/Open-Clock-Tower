namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求：面向**玩家**的投影——轮到某人做选择时，服务端主动推给他的一次请求。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0011 与 <c>docs/architecture/current.md</c> §2.7：与 <see cref="DecisionPoint"/>
/// 同源于 <see cref="ChoicePrompt"/>（同一个原语，受众与投递方式不同）。它由服务端发起，
/// 经 SignalR 定向推送到该玩家的连接；客户端不轮询、不自行推断轮次。
/// </para>
/// <para>
/// **硬约束**：这里没有"超时/过期时间"字段——请求一直有效，直到被响应或被上游变化作废；
/// 无超时的对冲是说书人强制作废 / 代填（D-0011 硬约束 3 与代价条款）。
/// </para>
/// </remarks>
public sealed record OperationRequest
{
    /// <summary>稳定标识，进事件流后永不改变。</summary>
    public required OperationRequestId Id { get; init; }

    /// <summary>这条请求是给谁的。</summary>
    public required SeatId Addressee { get; init; }

    /// <summary>
    /// 请求的来源：槽位（计划里的步骤槽位）或触发（能力在事件发生时开出）。
    /// 槽位来源随槽位推进了结、消耗夜晚配额；触发来源不占槽位、不消耗配额（R-0027）。
    /// </summary>
    public required OperationRequestOrigin Origin { get; init; }

    /// <summary>同源选择契约：上下文、合法选项、无合法选项时的行为。</summary>
    public required ChoicePrompt Prompt { get; init; }

    /// <summary>座位依赖：任何一条不满足即自动作废。</summary>
    public IReadOnlyList<SeatDependency> Dependencies { get; init; } = [];

    /// <summary>生命周期状态。</summary>
    public OperationRequestStatus Status { get; init; } = OperationRequestStatus.Pending;

    /// <summary>响应内容；仅 Answered 时非空。</summary>
    public OperationRequestAnswer? Answer { get; init; }

    /// <summary>作废内容；仅 Voided 时非空。</summary>
    public OperationRequestVoid? Voided { get; init; }
}
