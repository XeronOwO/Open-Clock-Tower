namespace OpenClockTower.Contracts;

/// <summary>重连包：快照 + 从客户端已知序号起的全部事件（D-0010 §5）。</summary>
public sealed record ReconnectBundleDto
{
    /// <summary>快照对应的最新序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>该玩家的最新视图。</summary>
    public required PlayerViewDto View { get; init; }

    /// <summary>序号大于客户端已知序号、且允许该玩家看到的事件（白名单投影）。</summary>
    public required PlayerEventDto[] Events { get; init; }
}
