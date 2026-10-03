namespace OpenClockTower.Kernel;

/// <summary>
/// 开一个面向说书人的裁定点：引擎算出「此处需要说书人决定」（R-0009）。
/// </summary>
/// <remarks>
/// 两类来源，与 <see cref="OperationRequestOrigin"/> 的槽位 / 触发对称：
/// 槽位来源（<see cref="SlotId"/> 非空，如入槽的裁定点、理发师的当夜交互）与
/// 触发来源（<see cref="TriggerAbility"/> 非空、无槽位，如心上人死亡即开的说书人选择，
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0039）——两者恰好一个非空。
/// </remarks>
public sealed record DecisionPointRaisedEvent : GameEvent
{
    /// <summary>来自哪个槽位；null = 触发来源（不由任何槽位承载、不参与槽位推进）。</summary>
    public StepSlotId? SlotId { get; init; }

    /// <summary>触发来源的能力归因（幂等 / 投影用）；槽位来源时为 null。</summary>
    public AbilityId? TriggerAbility { get; init; }

    /// <summary>
    /// 裁定点的**归属席位**：说书人视图「谁在等这一裁定」的唯一真相（触发格 / 触发型裁定没有
    /// 行动者或槽位，只能靠它归属）。规则上每个裁定点都能归属到一席（槽位来源 = 行动者；
    /// 触发来源 = 死亡 / 触发者本人），新开点一律显式给出；旧版本事件没有这个字段时为 null，
    /// 重放容忍（界面退回行动者 / 摘要回退，不猜）。
    /// </summary>
    public SeatId? AttributionSeat { get; init; }

    /// <summary>面向说书人的裁定点（与操作请求同源同一个 <see cref="ChoicePrompt"/>）。</summary>
    public required DecisionPoint DecisionPoint { get; init; }

    /// <summary>
    /// 本裁定点的提示要**同时替换槽位里的计划快照**时携带它（入槽实时重建的结果）；null = 不改
    /// （计划快照仍然有效）。槽位提示是这一步的操作上下文——视图「当前步骤」与后续消费者都读它，
    /// 不回写就会出现"裁定点是新值、摘要还是计划旧值"的分叉。
    /// </summary>
    public ChoicePrompt? SlotPrompt { get; init; }
}
