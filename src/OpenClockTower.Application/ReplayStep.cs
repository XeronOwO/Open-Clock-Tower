using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘时间轴上的一个原子步骤：一条事件一个步骤（显式排除项见 <see cref="ReplayStepCatalog"/>）。
/// </summary>
/// <remarks>
/// 顺序保真 = <see cref="Sequence"/> 严格递增且与事件流一致（D-0020：不跳步、不合并）。
/// <see cref="Summary"/> / <see cref="Detail"/> 由服务端按说书人账本口径生成（票据矩阵行 4），
/// 前端只做防御性呈现。
/// </remarks>
public sealed record ReplayStep
{
    /// <summary>背书事件序号（进度以它为准）。</summary>
    public required long Sequence { get; init; }

    /// <summary>呈现族（图标 / 配色 / 分组用；不参与规则判定）。</summary>
    public required ReplayStepKind Kind { get; init; }

    /// <summary>本步所属阶段；还未开局（开局分配等账事件）为 null。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>短文案：这一步发生了什么（行动者 / 目标 / 结果）。</summary>
    public required string Summary { get; init; }

    /// <summary>补充文案：原因 / 归因 / 备注；没有则为 null。</summary>
    public string? Detail { get; init; }

    /// <summary>本步引发的席位事实增量（未观测维度为 null）。</summary>
    public IReadOnlyList<ReplaySeatDelta> Seats { get; init; } = [];

    /// <summary>本步在圆盘上的可视化标记。</summary>
    public IReadOnlyList<ReplayMarker> Markers { get; init; } = [];
}
