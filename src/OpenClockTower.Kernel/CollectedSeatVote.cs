namespace OpenClockTower.Kernel;

/// <summary>一次逐席收票的冻结结论：分针指向该席那一刻的举手状态（R-0017 目标形态）。</summary>
/// <remarks>
/// 严格时点语义：先举也算、过时不候。席位收票后再改被 <see cref="DayMachine.CastVote"/> 拒绝；
/// 角色快照随结论落账（未观测为 null，不猜），供回溯型能力按"动作当时"推演（R-0037）。
/// </remarks>
public sealed record CollectedSeatVote
{
    /// <summary>被收票的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>冻结结论：true = 该席举手赞成；false = 未举手。</summary>
    public required bool Voted { get; init; }

    /// <summary>收票那一刻该席的角色快照（未观测为 null）。</summary>
    public CharacterId? VoterCharacter { get; init; }
}
