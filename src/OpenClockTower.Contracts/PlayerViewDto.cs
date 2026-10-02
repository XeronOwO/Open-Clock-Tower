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

    /// <summary>发给该玩家的信息类结果（他自己能力得到的信息），按发生顺序。</summary>
    public required IReadOnlyList<InformationResultDto> InformationResults { get; init; }

    /// <summary>白天投影（公开事实 + 自己能做什么）；还没有开过白天时为 null。</summary>
    public PlayerDayDto? Day { get; init; }
}
