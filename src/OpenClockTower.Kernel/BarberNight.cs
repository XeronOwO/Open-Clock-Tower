namespace OpenClockTower.Kernel;

/// <summary>
/// 「今晚理发」事实：理发师（未醉酒 / 未中毒）死亡后，恶魔在当晚可以选择两名玩家交换角色。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《理发师》· 2026-10-01 抓取 · 角色能力——「如果你死亡，在当晚恶魔可以选择两名玩家
/// （不能选择其他恶魔）交换角色」；《死亡触发能力》· 2026-10-01 抓取 · 能力简介——这类能力在死亡时
/// 立即触发，但涉及交互的效果「需要等到夜晚的时候再进行通知（角色或阵营变化）或交互」。
/// </para>
/// <para>
/// 平台口径（见 <c>docs/standard/rulings.md</c> R-0033）：死亡事件落账时**立即**记下事实；
/// 事实**跨白天 → 夜晚保留**（白天死亡 → 当夜交互），到夜晚计划走完仍未消费时由推进路径
/// **显式清空**并记「过时不候」。与只属于当夜的 <see cref="PitHagNight"/> 不同，
/// 它必须能穿过阶段边界，所以由 <see cref="StepMachineFolder"/> 在 <see cref="PhaseStartedEvent"/>
/// 折叠时继承；而夜晚计划结束时若仍挂着，则视为事件流收口缺失（显式失败，不静默顺延）。
/// </para>
/// </remarks>
public sealed record BarberNight
{
    /// <summary>以理发师身份死亡的席位（能力来源；角色交换的归因）。</summary>
    public required SeatId Source { get; init; }

    /// <summary>记账说明（谁以什么方式死亡触发），进审计与说书人视图。</summary>
    public required string Note { get; init; }
}
