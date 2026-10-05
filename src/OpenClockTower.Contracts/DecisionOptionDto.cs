namespace OpenClockTower.Contracts;

/// <summary>一个合法选项的线上形状。</summary>
/// <remarks>
/// 「真值 / 分组 / 事实编码 / 互斥组 / 徽章」是信息类候选（博学者 R-0057-C）才有的呈现元数据：真值由规则层
/// 的候选事实库按账求值，前端只显示、不推算（web/AGENTS.md §4）。普通候选这几项为 null / 空数组。
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

    /// <summary>候选事实编码（如 <c>demon-seat-parity</c>）；不适用时为 null。</summary>
    public string? Code { get; init; }

    /// <summary>
    /// 取值互斥组（如 <c>demon-seat-parity</c>）：同编码、同互斥组的两条候选必然一真一假，
    /// 说书人端据此在候选上预先灰掉那一条（服务端提交时仍会拒绝）；不适用时为 null。
    /// </summary>
    public string? ExclusionGroup { get; init; }

    /// <summary>候选徽章（如「高强度」）；没有徽章时为空数组。</summary>
    public string[] Tags { get; init; } = [];
}
