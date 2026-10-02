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

    /// <summary>当前「即将被处决」的席位；没有时为 null。</summary>
    public int? AboutToBeExecuted { get; init; }

    /// <summary>本白天实际被处决的席位；还没有处决时为 null。</summary>
    public int? Executed { get; init; }

    /// <summary>当前开放投票的提名序号；没有时为 null。</summary>
    public int? OpenNominationIndex { get; init; }
}
