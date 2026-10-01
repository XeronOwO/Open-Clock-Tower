namespace OpenClockTower.Kernel;

/// <summary>
/// 一次操作请求的响应内容。
/// </summary>
public sealed record OperationRequestAnswer
{
    /// <summary>被选中的选项值（必须来自请求的合法选项集合）。</summary>
    public required string OptionValue { get; init; }

    /// <summary>这次响应由谁给出。</summary>
    public required ResponseSource Source { get; init; }

    /// <summary>备注：说书人代填时写明代填缘由；玩家响应一般为空。</summary>
    public string? Note { get; init; }
}
