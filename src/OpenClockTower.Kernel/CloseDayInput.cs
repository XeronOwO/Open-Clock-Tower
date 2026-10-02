namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人结束白天：处决当前「即将被处决」者（如果有），然后关闭白天、走完白天计划。
/// </summary>
/// <remarks>
/// 依据百科《规则概要》三-3 · 2026-10-01 抓取：提名阶段结束时处决当前「即将被处决」的玩家；
/// 每个白天最多一次处决。提名还没计票时不能结束（先把票计完或强推兜底）。
/// </remarks>
public sealed record CloseDayInput : StepMachineInput;
