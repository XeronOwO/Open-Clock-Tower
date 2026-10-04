using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人视图：完整看板——阶段、当前槽位、控制模式、卡点、待裁定的裁定点、阻塞原因。
/// </summary>
/// <remarks>
/// 说书人是这台步骤机的最终裁量者（D-0014）：这里给足信息，兜底入口才有意义。
/// </remarks>
public sealed record StorytellerView
{
    /// <summary>视图对应的事件流序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>当前阶段；未开局为 null。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>当前控制模式；未开局为 null。</summary>
    public ControlMode? Control { get; init; }

    /// <summary>房间健康位：恢复 / 重建失败后为降级态（原因 + 发生时间）；正常为健康。</summary>
    public required RoomHealth Health { get; init; }

    /// <summary>当前槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>本计划的槽位总数。</summary>
    public required int SlotCount { get; init; }

    /// <summary>当前槽位标识；计划已走完为 null。</summary>
    public StepSlotId? CurrentSlotId { get; init; }

    /// <summary>本计划是否已走完。</summary>
    public required bool PlanCompleted { get; init; }

    /// <summary>卡点摘要；没有挂起请求时为 null。</summary>
    public PendingRequestSummary? Pending { get; init; }

    /// <summary>等待说书人裁定的裁定点（R-0009）；没有时为 null。</summary>
    public DecisionPoint? AwaitingDecision { get; init; }

    /// <summary>
    /// 挂起裁定点的**归属席位**（谁在等）：触发格 / 触发型裁定没有行动者或槽位，圆环只能靠它归属；
    /// 没有挂起时为 null。
    /// </summary>
    public SeatId? AwaitingDecisionSeat { get; init; }

    /// <summary>阻塞原因（R-0009 BlockAndAlert）；没有阻塞时为 null。</summary>
    public string? BlockedReason { get; init; }

    /// <summary>当前槽位的行动者（上帝视角）；空槽位 / 已走完为 null。</summary>
    public SeatId? CurrentSlotActor { get; init; }

    /// <summary>当前槽位请求的上下文（为什么要做这个选择）。</summary>
    public string? CurrentSlotContext { get; init; }

    /// <summary>最近的状态变化（含原因与归因，最新在后）。</summary>
    public required IReadOnlyList<SeatChangeSnapshot> RecentSeatChanges { get; init; }

    /// <summary>
    /// 状态账：每个已观测席位的六维度已知态与**逐维度归因**（谁、因何）。
    /// 这是上帝视角要回答"这一步为什么是这样"的地方；玩家投影里没有它（D-0012 §4.3）。
    /// </summary>
    public required IReadOnlyList<SeatStateEntry> Seats { get; init; }

    /// <summary>持续型效果（谁施加、用哪个能力、作用于谁、是否已终止及终止原因）。</summary>
    public required IReadOnlyList<PersistentEffect> PersistentEffects { get; init; }

    /// <summary>即时型效果（已生效即不回滚）。</summary>
    public required IReadOnlyList<InstantaneousEffect> InstantaneousEffects { get; init; }

    /// <summary>能力使用账本：用过没有、生效过没有（架构 §2.2）。</summary>
    public required IReadOnlyList<AbilityUse> AbilityUses { get; init; }

    /// <summary>失效账本：每次「能力未正常生效」及原因（R-0004；一次可并列多条，数学家按玩家去重要数字）。</summary>
    public required IReadOnlyList<Malfunction> Malfunctions { get; init; }

    /// <summary>最近一次能力结算的结论（是否生效、为什么没生效）；还没有结算过时为 null。</summary>
    public AbilityResolutionSnapshot? LastResolution { get; init; }

    /// <summary>每步摘要（当前槽位的行动者状态、能力判定与选项行为）；非行动槽位 / 已走完为 null。</summary>
    public StepDigest? StepDigest { get; init; }

    /// <summary>最近一次被作废的操作请求（含「哪条依赖不满足」的说明）；还没有作废过时为 null。</summary>
    public VoidedRequestSnapshot? LastVoidedRequest { get; init; }

    /// <summary>最新一天（进行中或最近结束）的白天账；还没有开过白天时为 null。</summary>
    public DayRecord? Day { get; init; }

    /// <summary>当前开放提名的钟盘收票呈现（相位 / 当前席位 / 已收席位 / 剩余时间）；没有收票时为 null。</summary>
    public VoteSweepView? VoteSweep { get; init; }

    /// <summary>胜负结论；null = 游戏仍在进行。结束后一切命令被拒（R-0024）。</summary>
    public GameOutcome? Outcome { get; init; }

    /// <summary>呆瓜的公开选择（含"没选"的跳过），按发生顺序（R-0027）。</summary>
    public IReadOnlyList<KlutzChoiceRecord> KlutzChoices { get; init; } = [];

    /// <summary>
    /// 麻脸巫婆之夜的死亡裁量窗口；null = 今晚没有（R-0030）。
    /// 说书人据此看到待定死亡与关闭点，并用「追加死亡 / 裁定待定死亡」两条命令收口。
    /// </summary>
    public PitHagNight? PitHagNight { get; init; }

    /// <summary>
    /// 方古的「限一次」整局事实（R-0034）；null = 还没用掉。
    /// 说书人据此在魔典中心显示「限一次」标记（百科《方古》· 提示标记）；玩家投影里没有它。
    /// </summary>
    public FangGuInfection? FangGuInfection { get; init; }

    /// <summary>
    /// 「今晚理发」待处理事实（R-0033）：理发师死亡后、恶魔在当夜交互；null = 没有待处理。
    /// 事实跨阶段保留，说书人据此知道"今晚还有一次理发交互"；玩家投影里没有它。
    /// </summary>
    public BarberNight? BarberNight { get; init; }

    /// <summary>
    /// 说书人注记（D-0019）：自由文本提示标记，按发生顺序。
    /// 它是独立注记账的投影（D-0015：自由文本不进状态账），玩家投影里没有它（D-0012 §4.3）。
    /// </summary>
    public IReadOnlyList<SeatAnnotation> Annotations { get; init; } = [];

    /// <summary>
    /// 本局公开的「席位 → 玩家名」映射（D-0021）：与玩家投影同一份会话读模型；
    /// 没有玩家名的席位（游客）不出现。姓名只作公开呈现，不参与任何判定。
    /// </summary>
    public IReadOnlyList<SeatDisplayName> SeatNames { get; init; } = [];

    /// <summary>
    /// 「失去能力」提示标记（R-0040）：限次能力用尽后挂在角色标记旁（由能力使用账本派生）。
    /// 玩家投影里没有它（D-0012 §4.3）；本人"已用尽"走玩家视图的窄字段。
    /// </summary>
    public IReadOnlyList<LostAbilityMarker> LostAbilityMarkers { get; init; } = [];
}
