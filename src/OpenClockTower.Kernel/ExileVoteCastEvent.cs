namespace OpenClockTower.Kernel;

/// <summary>某席位在当前开放的流放提议上举手 / 放下（R-0044 第 4 条）。</summary>
/// <remarks>
/// 流放不是投票：这个动作不计入「参与投票」，因此不落投票动作表、不带角色快照，
/// 也不触发卖花女孩一类投票感知能力（R-0044 第 1 条）。在局玩家（含死者）都可以举手；
/// 死者不查也不耗「死后仅一次」投票标记（R-0044 第 1 / 4 条）。
/// </remarks>
public sealed record ExileVoteCastEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>投的是当天第几条流放。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>举手 / 放下的席位。</summary>
    public required SeatId Voter { get; init; }

    /// <summary>true = 举手赞成；false = 放下。</summary>
    public required bool Voted { get; init; }
}
