namespace OpenClockTower.Contracts;

/// <summary>
/// 复盘时间轴上的一个原子步骤（= 一条事件；显式排除项见 D-0020）。
/// </summary>
/// <remarks>
/// 文案由服务端按说书人账本口径生成；前端只做防御性呈现，不推演规则（web/AGENTS.md §4）。
/// </remarks>
public sealed record ReplayStepDto
{
    /// <summary>背书事件序号（进度以它为准）。</summary>
    public required long Sequence { get; init; }

    /// <summary>呈现族（<c>ReplayStepKind</c> 的枚举名，如 <c>State</c> / <c>Decision</c>）。</summary>
    public required string Kind { get; init; }

    /// <summary>本步所属阶段（<c>GamePhase</c> 的枚举名）；未开局为 null。</summary>
    public string? Phase { get; init; }

    /// <summary>短文案：这一步发生了什么（行动者 / 目标 / 结果）。</summary>
    public required string Summary { get; init; }

    /// <summary>补充文案：原因 / 归因 / 备注；没有则为 null。</summary>
    public string? Detail { get; init; }

    /// <summary>本步引发的席位事实增量（未观测维度为 null）。</summary>
    public required ReplaySeatDeltaDto[] Seats { get; init; }

    /// <summary>本步在圆盘上的可视化标记（术语表 slug）。</summary>
    public required ReplayMarkerDto[] Markers { get; init; }
}
