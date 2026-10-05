namespace OpenClockTower.Contracts;

/// <summary>一个合法选项的线上形状。</summary>
/// <remarks>
/// 「真值 / 分组 / 徽章」是信息类候选（博学者 R-0057-C）才有的呈现元数据：真值由规则层的候选事实库
/// 按账求值，前端只显示、不推算（web/AGENTS.md §4）。普通候选这三项为 null / 空数组。
/// </remarks>
public sealed record DecisionOptionDto
{
    /// <summary>选项值。</summary>
    public required string Value { get; init; }

    /// <summary>后果预览。</summary>
    public required string Preview { get; init; }

    /// <summary>候选真值：<c>True</c> / <c>False</c>；与真假无关的候选为 null。</summary>
    public string? Truth { get; init; }

    /// <summary>候选分组（如「座位关系」）；不分组时为 null。</summary>
    public string? Group { get; init; }

    /// <summary>候选徽章（如「高强度」）；没有徽章时为空数组。</summary>
    public string[] Tags { get; init; } = [];
}
