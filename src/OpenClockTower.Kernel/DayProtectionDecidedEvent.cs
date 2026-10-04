namespace OpenClockTower.Kernel;

/// <summary>说书人裁定某席位「今天的死亡保护」（R-0048；折进当天账）。</summary>
/// <remarks>
/// 结论只记「受保护 / 不受保护」；角色语义（怪咖「今天是否有趣」）由说书人对着规则层提问回答。
/// 每次流放收口只读当天账里的裁定，不重算、不重问（D-0010：事件是唯一事实来源）。
/// </remarks>
public sealed record DayProtectionDecidedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>被裁定的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 今天受死亡保护；false = 不受保护。</summary>
    public required bool Protected { get; init; }

    /// <summary>说书人的说明（可选）。</summary>
    public string? Note { get; init; }
}
