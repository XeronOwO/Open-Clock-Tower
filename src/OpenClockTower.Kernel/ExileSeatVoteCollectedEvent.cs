namespace OpenClockTower.Kernel;

/// <summary>流放收票到点：第 N 席的举手状态被冻结成这一票（先举也算、过时不候；R-0017 口径）。</summary>
/// <remarks>
/// 严格时点语义与提名一致；冻结结论只进流放账，不写投票动作表——角色能力不能影响流放流程
/// （R-0044 第 1 / 8 条），没有回溯型能力需要读这个动作。
/// </remarks>
public sealed record ExileSeatVoteCollectedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几条流放。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>被收票的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>冻结结论：true = 该席举手赞成；false = 未举手（不计票）。</summary>
    public required bool Voted { get; init; }
}
