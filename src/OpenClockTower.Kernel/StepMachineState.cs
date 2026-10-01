namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机状态：**可持久化、可重放**的纯数据（D-0011 硬约束 4）。
/// </summary>
/// <remarks>
/// 挂起不是"内存里挂着一个长连接"，而是这里的 <see cref="PendingRequest"/> /
/// <see cref="AwaitingDecision"/> / <see cref="Block"/> 加上事件流：服务端重启后，
/// 重放事件即可恢复同样的挂起状态，玩家重连即可响应。
/// </remarks>
public sealed record StepMachineState
{
    /// <summary>当前阶段计划（含空槽位）。</summary>
    public required StepPlan Plan { get; init; }

    /// <summary>当前槽位下标；等于 <see cref="StepPlan.Slots"/> 数量表示本计划已走完。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>当前槽位的最短配额是否已走完。</summary>
    public required SlotQuotaState Quota { get; init; }

    /// <summary>自动 / 说书人接管。</summary>
    public required ControlMode Control { get; init; }

    /// <summary>等待玩家响应的请求；null 表示没有。</summary>
    public OperationRequest? PendingRequest { get; init; }

    /// <summary>等待说书人裁定的裁定点（R-0009 StorytellerDecides）；null 表示没有。</summary>
    public DecisionPoint? AwaitingDecision { get; init; }

    /// <summary>阻塞报警（R-0009 BlockAndAlert）；null 表示没有。</summary>
    public StepBlock? Block { get; init; }

    /// <summary>当前槽位；计划已走完时为 null。</summary>
    public StepSlot? CurrentSlot =>
        SlotIndex >= 0 && SlotIndex < Plan.Slots.Count ? Plan.Slots[SlotIndex] : null;

    /// <summary>本计划是否已走完。</summary>
    public bool IsPlanCompleted => SlotIndex >= Plan.Slots.Count;

    /// <summary>是否有挂起（等玩家 / 等说书人 / 阻塞）——自动推进必须等它了结。</summary>
    public bool IsHeld =>
        PendingRequest is { Status: OperationRequestStatus.Pending }
        || AwaitingDecision is not null
        || Block is not null;
}
