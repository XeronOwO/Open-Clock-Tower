namespace OpenClockTower.Kernel;

/// <summary>
/// 一名旅行者离开本局：席位与票据保留，但**不再计入任何「人数」口径**。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《旅行者》（2026-10-04 抓取 · 旅行者运作方式）：离开 = 「在魔典中移除他的角色标记，
/// 并在城镇广场中移除他的生命标记」；平台口径见 `rulings.md` R-0044 第 6 条——离场者不计入
/// 流放分母、投票、胜负等任何人数口径；席位与座位号保留（不删席位，重连与复盘语义不动）。
/// </para>
/// <para>
/// 折叠口径（<see cref="GameStateMachine"/>）：席位账移除、离场账登记、以该席位为来源 / 目标的
/// 持续型效果与疯狂要求立即终止。
/// </para>
/// </remarks>
public sealed record TravellerDepartedEvent : GameEvent
{
    /// <summary>离场的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>说书人给出的离场说明（审计与复盘的追因；可空）。</summary>
    public string? Note { get; init; }
}
