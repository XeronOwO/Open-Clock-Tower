namespace OpenClockTower.Kernel;

/// <summary>无合法选项且声明为说书人自由决定（R-0009）：把同一个选择契约投影给说书人。</summary>
public sealed record DecisionPointRaisedEvent : GameEvent
{
    /// <summary>来自哪个槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>面向说书人的裁定点（与操作请求同源同一个 <see cref="ChoicePrompt"/>）。</summary>
    public required DecisionPoint DecisionPoint { get; init; }
}
