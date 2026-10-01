namespace OpenClockTower.Contracts;

/// <summary>一个合法选项的线上形状。</summary>
public sealed record DecisionOptionDto
{
    /// <summary>选项值。</summary>
    public required string Value { get; init; }

    /// <summary>后果预览。</summary>
    public required string Preview { get; init; }
}
