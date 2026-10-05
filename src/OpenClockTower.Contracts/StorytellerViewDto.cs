namespace OpenClockTower.Contracts;

/// <summary>说书人视图：完整看板 + 兜底所需的一切（D-0014）。</summary>
public sealed record StorytellerViewDto
{
    /// <summary>视图对应的最新序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>当前大阶段。</summary>
    public required string Phase { get; init; }

    /// <summary>控制模式：Automatic / StorytellerTakeover。</summary>
    public required string Control { get; init; }

    /// <summary>房间健康位：恢复 / 重建失败后的降级状态（原因 + 发生时间）；正常时 Degraded=false。</summary>
    public required RoomHealthDto Health { get; init; }

    /// <summary>当前槽位下标。</summary>
    public required int SlotIndex { get; init; }

    /// <summary>槽位总数。</summary>
    public required int SlotCount { get; init; }

    /// <summary>当前槽位标识；计划已走完为 null。</summary>
    public string? CurrentSlotId { get; init; }

    /// <summary>本计划是否已走完。</summary>
    public required bool PlanCompleted { get; init; }

    /// <summary>卡点摘要。</summary>
    public PendingRequestDto? Pending { get; init; }

    /// <summary>等待说书人裁定的裁定点标识；没有时为 null。</summary>
    public string? AwaitingDecisionId { get; init; }

    /// <summary>等待裁定的上下文（为什么需要说书人决定，D-0002）。</summary>
    public string? AwaitingDecisionContext { get; init; }

    /// <summary>等待裁定的合法选项（引擎算出的候选）；无选项的裁定点为 null。</summary>
    public DecisionOptionDto[]? AwaitingDecisionOptions { get; init; }

    /// <summary>
    /// 待裁定裁定点的真值组合约束（博学者 R-0057 / 涡流 R-0028）：候选带真值的裁定点才有，
    /// 取值为 <c>ExactlyOneTrue</c> / <c>AllFalse</c> / <c>AnyCombination</c> / <c>Indeterminate</c>；
    /// 其余裁定点为 null。前端据此显示组合结论，服务端在提交时用同一条声明重新核对。
    /// </summary>
    public string? AwaitingDecisionTruthRule { get; init; }

    /// <summary>真值组合约束的说明（给说书人看的原因与依据）；不适用时为 null。</summary>
    public string? AwaitingDecisionTruthNote { get; init; }

    /// <summary>等待裁定的归属席位（"谁在等"）；没有挂起裁定时为 null。触发格 / 触发型裁定靠它归属。</summary>
    public int? AwaitingDecisionSeat { get; init; }

    /// <summary>阻塞原因；没有阻塞时为 null。</summary>
    public string? BlockedReason { get; init; }

    /// <summary>当前槽位的行动者（上帝视角）；空槽位 / 已走完为 null。</summary>
    public int? CurrentSlotActor { get; init; }

    /// <summary>当前槽位请求的上下文。</summary>
    public string? CurrentSlotContext { get; init; }

    /// <summary>最近的状态变化（含原因与归因，最新在后）。</summary>
    public required SeatChangeDto[] RecentSeatChanges { get; init; }

    /// <summary>状态账：每个已观测席位的六维度已知态与逐维度归因。</summary>
    public required SeatStateDto[] Seats { get; init; }

    /// <summary>效果归因链（含已终止的效果）。</summary>
    public required EffectDto[] Effects { get; init; }

    /// <summary>能力使用账本（用过没有 / 生效过没有，架构 §2.2）。</summary>
    public required AbilityUseDto[] AbilityUses { get; init; }

    /// <summary>失效账本（未正常生效及原因分类，R-0004）。</summary>
    public required MalfunctionDto[] Malfunctions { get; init; }

    /// <summary>最近一次能力结算的结论；还没有结算过时为 null。</summary>
    public AbilityResolutionDto? LastResolution { get; init; }

    /// <summary>每步摘要（当前槽位的行动者状态、能力判定与选项行为）；非行动槽位 / 已走完为 null。</summary>
    public StepDigestDto? StepDigest { get; init; }

    /// <summary>最近一次被作废的操作请求（含「哪条依赖不满足」的说明）；还没有作废过时为 null。</summary>
    public OperationRequestVoidedDto? LastVoidedRequest { get; init; }

    /// <summary>最新一天（进行中或最近结束）的白天账；还没有开过白天时为 null。</summary>
    public DayViewDto? Day { get; init; }

    /// <summary>胜负结论；null = 游戏仍在进行。结束后一切命令被拒（R-0024）。</summary>
    public GameOutcomeDto? Outcome { get; init; }

    /// <summary>呆瓜的公开选择（含跳过），按发生顺序（R-0027）。</summary>
    public required KlutzChoiceDto[] KlutzChoices { get; init; }

    /// <summary>本局公开的「席位 → 玩家名」映射（D-0021；无玩家名的席位不出现，与玩家投影同一份）。</summary>
    public required SeatDisplayNameDto[] SeatNames { get; init; }

    /// <summary>麻脸巫婆之夜的死亡裁量窗口；null = 今晚没有（R-0030）。只说书人可见。</summary>
    public PitHagNightDto? PitHagNight { get; init; }

    /// <summary>方古的「限一次」整局事实；null = 还没用掉（R-0034）。只说书人可见。</summary>
    public FangGuInfectionDto? FangGuInfection { get; init; }

    /// <summary>「今晚理发」待处理事实；null = 没有待处理（R-0033）。只说书人可见。</summary>
    public BarberNightDto? BarberNight { get; init; }

    /// <summary>死亡保护裁定提示（R-0048）：只在这一席此刻真能被裁定时非 null。只说书人可见。</summary>
    public DayProtectionPromptDto? PendingProtection { get; init; }

    /// <summary>说书人注记（D-0019）：自由文本提示标记，按发生顺序；玩家投影里没有它。</summary>
    public required SeatAnnotationDto[] Annotations { get; init; }

    /// <summary>「失去能力」提示标记（R-0040）：限次能力用尽后挂在角色标记旁；玩家投影里没有它。</summary>
    public LostAbilityMarkerDto[] LostAbilityMarkers { get; init; } = [];
}
