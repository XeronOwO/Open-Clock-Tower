namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人对某席位「今天的死亡保护」的裁定（折进 <see cref="DayRecord.ProtectionDecisions"/>；R-0048）。
/// </summary>
/// <remarks>
/// 保护是**按天**生效的（怪咖：当天不能被流放）：同一天同一席位至多一条裁定，第二天重新裁定。
/// 记录的是裁定结论本身（受保护 / 不受保护），不是角色语义（「今天是否有趣」由说书人对着规则层提问回答，
/// 平台只承载结论——与 <see cref="DeferredDeathResolvedEvent"/> 同一姿态）。
/// </remarks>
public sealed record DayProtectionDecision
{
    /// <summary>被裁定的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 今天受死亡保护（不产生死亡）；false = 不受保护。</summary>
    public required bool Protected { get; init; }
}
