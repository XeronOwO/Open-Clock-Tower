namespace OpenClockTower.Kernel;

/// <summary>
/// 「今晚理发」事实关闭：恶魔执行了交换、或放弃（摇头）、或请求被作废、或夜晚走完仍未等到交互。
/// </summary>
/// <remarks>
/// 关闭后本夜不再重复开请求；说明一律写进 <see cref="Note"/>（含「过时不候」的收口原因），
/// 进审计与说书人视图——不是静默清空（平台口径见 <c>docs/standard/rulings.md</c> R-0033）。
/// </remarks>
public sealed record BarberNightClosedEvent : GameEvent
{
    /// <summary>收口说明（交换结果 / 放弃 / 作废 / 过时不候的原因）。</summary>
    public required string Note { get; init; }
}
