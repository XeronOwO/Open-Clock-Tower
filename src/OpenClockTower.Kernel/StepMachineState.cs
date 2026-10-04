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

    /// <summary>
    /// 当前槽位的进入次数：0 = 还没进入；1 = 首次；2 及以上 = 同格重进的后续遍次。
    /// </summary>
    /// <remarks>
    /// 由事件流折叠派生（<see cref="SlotEnteredEvent"/> 加一、推进到下一格清零），不是新的事实来源。
    /// 操作请求 / 裁定点的稳定标识按它带遍次（<c>…:slot#2</c>），「行动两次」据此只重进一次——
    /// 口径见 <c>docs/standard/rulings.md</c> R-0052 第 2 条。
    /// </remarks>
    public int SlotPass { get; init; }

    /// <summary>
    /// 当前槽位是否已经产出过能力结算（<see cref="AbilityResolvedEvent"/> 已折入）。
    /// </summary>
    /// <remarks>
    /// 「行动两次」的第二次结算以它为凭据：被跳过 / 被作废 / 被阻塞的格子没有能力可再结算一次，
    /// 玩家摇头不算使用（<see cref="IAbilityResolution.CountsAsUse"/> 为 false 时不产结算事件）。
    /// 同样由事件流折叠派生，重放稳定。
    /// </remarks>
    public bool SlotAbilityResolved { get; init; }

    /// <summary>自动 / 说书人接管。</summary>
    public required ControlMode Control { get; init; }

    /// <summary>等待玩家响应的请求；null 表示没有。</summary>
    public OperationRequest? PendingRequest { get; init; }

    /// <summary>等待说书人裁定的裁定点（R-0009 StorytellerDecides）；null 表示没有。</summary>
    public DecisionPoint? AwaitingDecision { get; init; }

    /// <summary>
    /// 挂起裁定点的**触发来源**归因（如心上人死亡触发的说书人选择）；null = 槽位来源（或没有挂起）。
    /// 与 <see cref="AwaitingDecision"/> 同步置位 / 清空：推进闸用它区分触发型挂起（R-0039）。
    /// </summary>
    public AbilityId? AwaitingDecisionTriggerAbility { get; init; }

    /// <summary>
    /// 挂起裁定点的**归属席位**（说书人视图「谁在等」的呈现依据）；与
    /// <see cref="AwaitingDecision"/> 同步置位 / 清空。
    /// </summary>
    /// <remarks>
    /// 触发格与触发型裁定没有行动者 / 槽位，归属只能由开点来源显式给出（见
    /// <see cref="DecisionPointRaisedEvent.AttributionSeat"/>）。
    /// </remarks>
    public SeatId? AwaitingDecisionSeat { get; init; }

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

    /// <summary>
    /// 方古的「限一次」事实：本局已经完成过一次外来者侵染；null = 还没用掉。
    /// </summary>
    /// <remarks>
    /// 跨**整局**保留（同 <see cref="KlutzChoices"/> 的幂等账）：标记持续到游戏结束，即使原方古死亡 /
    /// 换角、或之后出现新的方古也不再侵染（百科《方古》· 2026-10-01 抓取 · 运作方式 14）。
    /// 口径见 <c>docs/standard/rulings.md</c> R-0034。
    /// </remarks>
    public FangGuInfection? FangGuInfection { get; init; }

    /// <summary>
    /// 「今晚理发」事实：理发师死亡后待恶魔在当夜交互；null = 没有待处理的理发师之夜。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PitHagNight"/> 不同，它**跨阶段保留**：白天死亡要在当夜交互
    /// （百科《死亡触发能力》），因此 <see cref="PhaseStartedEvent"/> 折叠时从上一状态继承；
    /// 夜晚计划走完仍未消费时由推进路径显式清空（过时不候）。口径见
    /// <c>docs/standard/rulings.md</c> R-0033。
    /// </remarks>
    public BarberNight? BarberNight { get; init; }

    /// <summary>
    /// 贤者「被恶魔杀死」的待展示事实：死亡批记下、当夜贤者触发格开裁定；null = 没有待处理事实。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="BarberNight"/> 同族：事实只属于当夜，夜晚计划走完仍未消费时由推进路径
    /// 显式清空并记「过时不候」；阶段边界上仍挂着即视为事件流收口缺失（显式失败，不顺延）。
    /// 口径见 <c>docs/standard/rulings.md</c> R-0038。
    /// </remarks>
    public SageNight? SageNight { get; init; }

    /// <summary>
    /// 心上人的死亡触发跳过账（能力未生效 / 说书人未裁定），按发生顺序。
    /// 触发器的幂等依据——有记录之后不再为同一名心上人的死亡重复求值（R-0039）。
    /// </summary>
    public IReadOnlyList<SweetheartSkipRecord> SweetheartSkips { get; init; } = [];

    /// <summary>
    /// 艺术家的进行中提问（白天主动问说书人）；null = 没有进行中的问题（R-0040）。
    /// </summary>
    /// <remarks>
    /// 只属于当前阶段：结清（回答 / 要求重问 / 强推作废）后清空；阶段边界上仍挂着即视为
    /// 收口缺失（显式失败，不顺延——与 <see cref="BarberNight"/> / <see cref="SageNight"/> 同族）。
    /// </remarks>
    public ArtistQuestion? ArtistQuestion { get; init; }

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
