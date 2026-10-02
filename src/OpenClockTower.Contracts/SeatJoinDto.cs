namespace OpenClockTower.Contracts;

/// <summary>玩家加入结果：连接级凭据 + 重连包（凭据只在签发它的那条连接上有效，D-0012）。</summary>
public sealed record SeatJoinDto
{
    /// <summary>连接级私有凭据；后续每条命令都必须随身出示。</summary>
    public required string Credential { get; init; }

    /// <summary>重连包：快照 + 从该序号起的事件。</summary>
    public required ReconnectBundleDto Bundle { get; init; }
}
