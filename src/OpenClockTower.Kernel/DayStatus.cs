namespace OpenClockTower.Kernel;

/// <summary>
/// 一天的状态：白天进行中 / 已结束。
/// </summary>
/// <remarks>
/// 依据百科《规则概要》三 · 2026-10-01 抓取：处决结束后白天阶段随即结束；
/// 白天也可能以"无人被处决"收尾，同样是结束。
/// </remarks>
public enum DayStatus
{
    /// <summary>白天进行中（提名 / 投票窗口开着）。</summary>
    Open,

    /// <summary>白天已结束（已处决或确认无人被处决），可以进入夜晚。</summary>
    Closed,
}
