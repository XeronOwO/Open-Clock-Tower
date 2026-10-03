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

    /// <summary>胜负结论；null = 游戏仍在进行。结束后对全体玩家一致可见（R-0024）。</summary>
    public GameOutcomeDto? Outcome { get; init; }

    /// <summary>呆瓜的公开选择（含跳过），按发生顺序（R-0027）。</summary>
    public required KlutzChoiceDto[] KlutzChoices { get; init; }

    /// <summary>本人进行中的艺术家提问全文；null = 没有（R-0040）。只对本人生效。</summary>
    public string? PendingQuestion { get; init; }

    /// <summary>本人此刻能不能发起艺术家的白天提问（白天开着、本人是艺术家且还没用过）。只对本人生效。</summary>
    public bool CanAskArtistQuestion { get; init; }

    /// <summary>本人已经用尽的一次性能力 slug（R-0040）；只列本人的。</summary>
    public string[] ExhaustedAbilities { get; init; } = [];
}
