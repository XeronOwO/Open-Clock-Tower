namespace OpenClockTower.Contracts;

/// <summary>
/// 复盘步骤在圆盘上的可视化标记（术语表 slug：<c>kill-arrow</c> / <c>shroud</c> /
/// <c>character-change</c> / <c>role-rebind</c> / <c>poisoned</c> / <c>drunk</c>）。
/// </summary>
public sealed record ReplayMarkerDto
{
    /// <summary>标记类别 slug（术语表口径；未知取值前端原样降级）。</summary>
    public required string Kind { get; init; }

    /// <summary>标记落在哪个席位；无席位为 null。</summary>
    public int? Seat { get; init; }

    /// <summary>箭头类标记的起点席位。</summary>
    public int? From { get; init; }

    /// <summary>箭头类标记的终点席位。</summary>
    public int? To { get; init; }

    /// <summary>可选的补充文本。</summary>
    public string? Text { get; init; }
}
