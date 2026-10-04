namespace OpenClockTower.Contracts;

/// <summary>白天公开事实（最新一天：进行中或最近结束）。</summary>
public sealed record DayViewDto
{
    /// <summary>第几天（1 = 首个白天）。</summary>
    public required int DayNumber { get; init; }

    /// <summary>Open（进行中）/ Closed（已结束）。</summary>
    public required string Status { get; init; }

    /// <summary>当天已发起的提名，按发生顺序。</summary>
    public required DayNominationDto[] Nominations { get; init; }

    /// <summary>当天已发起的流放提议，按发生顺序（旅行者流程；R-0044）。</summary>
    public required DayExileDto[] Exiles { get; init; }

    /// <summary>当前未结清的流放（表决中的那一条）序号；没有时为 null。</summary>
    public int? OpenExileIndex { get; init; }

    /// <summary>当天已裁定的死亡保护，按裁定顺序（每席位至多一条；R-0048）。</summary>
    public required DayProtectionDto[] Protections { get; init; }

    /// <summary>当天打开的额外提名窗口（屠夫）；null = 没有窗口（R-0050）。</summary>
    public DayExtraNominationDto? ExtraNomination { get; init; }

    /// <summary>当前「即将被处决」的席位；没有时为 null。</summary>
    public int? AboutToBeExecuted { get; init; }

    /// <summary>本白天实际被处决的席位；还没有处决时为 null。</summary>
    public int? Executed { get; init; }

    /// <summary>当前开放投票的提名序号；没有时为 null。</summary>
    public int? OpenNominationIndex { get; init; }
}
