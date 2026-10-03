namespace OpenClockTower.Kernel;

/// <summary>无合法选项且声明为说书人自由决定（R-0009）：把同一个选择契约投影给说书人。</summary>
public sealed record DecisionPointRaisedEvent : GameEvent
{
    /// <summary>来自哪个槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>面向说书人的裁定点（与操作请求同源同一个 <see cref="ChoicePrompt"/>）。</summary>
    public required DecisionPoint DecisionPoint { get; init; }

    /// <summary>
    /// 本裁定点的提示要**同时替换槽位里的计划快照**时携带它（入槽实时重建的结果）；null = 不改
    /// （计划快照仍然有效）。槽位提示是这一步的操作上下文——视图「当前步骤」与后续消费者都读它，
    /// 不回写就会出现"裁定点是新值、摘要还是计划旧值"的分叉。
    /// </summary>
    public ChoicePrompt? SlotPrompt { get; init; }
}
