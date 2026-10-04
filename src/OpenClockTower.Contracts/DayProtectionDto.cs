namespace OpenClockTower.Contracts;

/// <summary>当天说书人已裁定的死亡保护（公开账目；R-0048）。</summary>
/// <remarks>
/// 保护裁定只在该席位的流放「收票已收完、票面达线、目标存活」时才被受理（达线时裁定）；
/// 每席位每天至多一条、随事件流重放稳定（R-0048 第 2 条）。结论落在流放的
/// <see cref="DayExileDto.Conclusion"/>（<c>Protected</c>）上，这里保留裁定本身。
/// </remarks>
public sealed record DayProtectionDto
{
    /// <summary>被裁定的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>true = 今天受死亡保护（不因流放死亡）；false = 不受保护。</summary>
    public required bool Protected { get; init; }
}
