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

    /// <summary>
    /// 白天账（跨阶段保留）：逐日提名 / 投票 / 处决的事实与死亡玩家已消耗的投票权。
    /// 详见 <see cref="DayState"/>；夜晚阶段它保持原样，不被清空。
    /// </summary>
    public DayState? Day { get; init; }

    /// <summary>
    /// 胜负结论；null = 游戏仍在进行。折进本状态：快照已持久化步骤机，
    /// 结束后一切命令被拒（口径见 <c>docs/standard/rulings.md</c> R-0024）。
    /// </summary>
    public GameOutcome? Outcome { get; init; }

    /// <summary>
    /// 呆瓜选择的账目（含"没选"的跳过），按发生顺序。
    /// 触发器的幂等依据——有记录之后不再为同一名呆瓜重复开选择（R-0027）。
    /// </summary>
    public IReadOnlyList<KlutzChoiceRecord> KlutzChoices { get; init; } = [];

    /// <summary>
    /// 麻脸巫婆之夜的死亡裁量窗口；null = 今晚没有这个窗口（口径见 <c>rulings.md</c> R-0030）。
    /// </summary>
    /// <remarks>
    /// 只属于那一夜：新阶段由 <see cref="PhaseStartedEvent"/> 折叠成全新状态，不会继承它。
    /// 窗口关闭（越过最后一个能造成死亡的恶魔行动）时清空，未裁定的待定死亡按默认结果生效。
    /// </remarks>
    public PitHagNight? PitHagNight { get; init; }

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
