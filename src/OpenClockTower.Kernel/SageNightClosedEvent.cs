namespace OpenClockTower.Kernel;

/// <summary>
/// 贤者事实关闭：说书人完成了展示、或强推 / 收口未展示、或夜晚走完仍未消费（过时不候）。
/// </summary>
/// <remarks>
/// 关闭后本夜不再重复开裁定；说明一律写进 <see cref="Note"/>，进审计与说书人视图——
/// 不是静默清空（平台口径见 <c>docs/standard/rulings.md</c> R-0038）。
/// </remarks>
public sealed record SageNightClosedEvent : GameEvent
{
    /// <summary>收口说明（展示结果 / 未展示 / 过时不候的原因）。</summary>
    public required string Note { get; init; }
}
