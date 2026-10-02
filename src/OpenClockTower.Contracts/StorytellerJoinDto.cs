namespace OpenClockTower.Contracts;

/// <summary>说书人加入结果：连接级凭据 + 首份视图（凭据只在签发它的那条连接上有效，D-0012）。</summary>
public sealed record StorytellerJoinDto
{
    /// <summary>连接级私有凭据；后续每条命令与视图查询都必须随身出示。</summary>
    public required string Credential { get; init; }

    /// <summary>说书人视图：完整看板 + 兜底所需的一切（D-0014）。</summary>
    public required StorytellerViewDto View { get; init; }
}
