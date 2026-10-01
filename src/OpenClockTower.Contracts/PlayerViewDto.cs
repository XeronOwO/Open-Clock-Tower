namespace OpenClockTower.Contracts;

/// <summary>
/// 玩家视图：只有他自己的席位、当前大阶段与他自己的挂起请求。
/// </summary>
public sealed record PlayerViewDto
{
    /// <summary>席位。</summary>
    public required int Seat { get; init; }

    /// <summary>当前大阶段（昼夜属公开信息）。</summary>
    public required string Phase { get; init; }

    /// <summary>发给该玩家的挂起请求；没有时为 null。</summary>
    public OperationRequestDto? PendingRequest { get; init; }
}
