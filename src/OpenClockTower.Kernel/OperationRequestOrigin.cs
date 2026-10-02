namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求的来源：槽位来源（计划里的步骤槽位）或触发来源（能力在某条事件发生时开出）。
/// </summary>
/// <remarks>
/// <para>
/// 两种来源共用**同一条请求生命周期**（D-0011：无超时、定向推送、重连重投、说书人可代填 / 作废），
/// 区别只在"请求从哪来"：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="OperationRequestOriginKind.Slot"/>：随槽位推进了结、消耗夜晚配额（D-0013）；</description></item>
/// <item><description><see cref="OperationRequestOriginKind.Trigger"/>：不由任何槽位承载，因此**不消耗配额、
/// 也不参与槽位推进**（例如呆瓜死亡后的公开选择，R-0027）。</description></item>
/// </list>
/// <para>
/// 刻意用**具体类型 + 可空字段**而不是继承体系：步骤机状态要能作为快照持久化（JSON 往返），
/// 而内核不依赖任何序列化框架的注解（架构 §1）。
/// </para>
/// </remarks>
public sealed record OperationRequestOrigin
{
    /// <summary>来源类别。</summary>
    public required OperationRequestOriginKind Kind { get; init; }

    /// <summary>槽位标识（<see cref="OperationRequestOriginKind.Slot"/> 时非空）。</summary>
    public StepSlotId? SlotId { get; init; }

    /// <summary>计划标签（槽位来源；说书人视角用，不随玩家投影下发）。</summary>
    public string? PlanLabel { get; init; }

    /// <summary>计划里的第几个槽位（槽位来源）。</summary>
    public int? SlotIndex { get; init; }

    /// <summary>触发它的能力（触发来源；归因 / 幂等 / 投影用）。</summary>
    public AbilityId? TriggerAbility { get; init; }

    /// <summary>触发说明（触发来源；说书人视角用）。</summary>
    public string? TriggerReason { get; init; }

    /// <summary>构造一个槽位来源。</summary>
    public static OperationRequestOrigin ForSlot(StepSlotId slotId, string planLabel, int slotIndex) =>
        new()
        {
            Kind = OperationRequestOriginKind.Slot,
            SlotId = slotId,
            PlanLabel = planLabel,
            SlotIndex = slotIndex,
        };

    /// <summary>构造一个触发来源。</summary>
    public static OperationRequestOrigin ForTrigger(AbilityId ability, string reason) =>
        new()
        {
            Kind = OperationRequestOriginKind.Trigger,
            TriggerAbility = ability,
            TriggerReason = reason,
        };
}
