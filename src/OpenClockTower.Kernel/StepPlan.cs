namespace OpenClockTower.Kernel;

/// <summary>
/// 一个阶段的步骤表：有序槽位 + 阶段 + 计划标识。
/// </summary>
/// <remarks>
/// 计划由上层（Rules / Application）按剧本的完整顺序表构造；Kernel 只负责按它推进，
/// 不按在场角色增删槽位（D-0013 §1）。<see cref="Label"/> 在同一局内唯一，
/// 操作请求与裁定点的稳定标识由它导出。
/// </remarks>
public sealed record StepPlan
{
    /// <summary>计划标识（如 <c>sv:night-1</c>）。</summary>
    public required string Label { get; init; }

    /// <summary>该计划所属阶段。</summary>
    public required GamePhase Phase { get; init; }

    /// <summary>
    /// 建表口径标签（由上层规则层填入，如 <c>Original</c> / <c>Recommended</c>）；
    /// null = 未注明（测试夹具或占位数据）。Kernel 只记录与透传，不解释取值：
    /// R-0014 要求「本局实际口径」有处可查，<see cref="PhaseStartedEvent"/> 存下计划即留下这条记录。
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>有序槽位表；顺序就是夜晚顺序（含空槽位与黎明等待）。</summary>
    public required IReadOnlyList<StepSlot> Slots { get; init; }
}
