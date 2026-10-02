namespace OpenClockTower.Contracts;

/// <summary>推给玩家的"请求已被响应"（玩家本人作答或说书人代填）。</summary>
/// <remarks>
/// 与重连补齐里的 <c>PlayerEvent</c>（RequestAnswered）是**同一事实**：在线推送与重连补齐
/// 携带同样的字段，避免两条路径各自演化出不同口径（D-0010 / 架构 §5）。
/// </remarks>
public sealed record OperationRequestAnsweredDto
{
    /// <summary>产生这次响应的事件流序号（客户端据此与快照序号比较先后）。</summary>
    public required long Sequence { get; init; }

    /// <summary>被响应的请求标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>被选中的选项值（必须来自请求的合法选项集合）。</summary>
    public required string OptionValue { get; init; }

    /// <summary>响应来源（ResponseSource 名：Player / StorytellerProxy）。</summary>
    public required string Source { get; init; }

    /// <summary>说明：说书人代填时写明代填缘由；玩家响应一般为空。</summary>
    public string? Note { get; init; }
}
