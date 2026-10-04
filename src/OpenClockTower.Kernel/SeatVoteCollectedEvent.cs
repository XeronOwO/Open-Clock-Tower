namespace OpenClockTower.Kernel;

/// <summary>钟盘收票到点：第 N 席的举手状态被冻结成这一票（R-0017 目标形态）。</summary>
/// <remarks>
/// 严格时点：分针指向该席的那一刻以"已登记的举手状态"为准——先举也算、过时不候；
/// 本席收票后 <see cref="DayMachine.CastVote"/> 拒绝任何改动。
/// </remarks>
public sealed record SeatVoteCollectedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>第几次提名。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>被收票的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>冻结结论：true = 举手赞成；false = 未举手（不计票）。</summary>
    public required bool Voted { get; init; }

    /// <summary>收票那一刻该席的角色快照（未观测为 null，不猜）。</summary>
    public CharacterId? VoterCharacter { get; init; }
}
