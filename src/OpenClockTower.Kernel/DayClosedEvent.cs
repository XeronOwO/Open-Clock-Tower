namespace OpenClockTower.Kernel;

/// <summary>白天阶段结束（已处决或无人被处决），可以进入夜晚。</summary>
/// <remarks>
/// 依据百科《规则概要》三-3 · 2026-10-01 抓取：处决结束后白天阶段随即结束；
/// 无人被处决的白天同样结束。
/// </remarks>
public sealed record DayClosedEvent : GameEvent
{
    /// <summary>结束的是第几天。</summary>
    public required int DayNumber { get; init; }
}
