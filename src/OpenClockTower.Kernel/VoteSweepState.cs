namespace OpenClockTower.Kernel;

/// <summary>
/// 一次提名的钟盘收票状态（R-0017 目标形态）：席位顺序、节奏参数（只作呈现记录）与已收票结论。
/// </summary>
/// <remarks>
/// <para>
/// 内核不读时间：倒计时与间隔只是随事件流记录的**呈现参数**；"第几席到点"由控制面翻译成
/// <see cref="CollectSeatVoteInput"/>，内核只负责按顺序冻结结论（D-0008 / D-0010）。
/// </para>
/// <para>
/// <see cref="Seats"/> 是开始收票那一刻的完整座次快照：折叠层与回放据此校验收票顺序与完整性，
/// 不必再读会话名单；玩家座次整局不变，快照不随角色 / 生死维度变化。
/// </para>
/// </remarks>
public sealed record VoteSweepState
{
    /// <summary>收票顺序（开始收票时的完整座次，按席位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>倒计时长度（毫秒；呈现参数，判定不读）。</summary>
    public required int CountdownMilliseconds { get; init; }

    /// <summary>逐席间隔（毫秒；呈现参数，判定不读）。</summary>
    public required int IntervalMilliseconds { get; init; }

    /// <summary>已收票的席位结论，按收票顺序。</summary>
    public IReadOnlyList<CollectedSeatVote> Collected { get; init; } = [];

    /// <summary>下一待收席位；全部收完为 null。</summary>
    public SeatId? NextSeat => Collected.Count < Seats.Count ? Seats[Collected.Count] : null;

    /// <summary>是否全部收完（可以计票）。</summary>
    public bool IsComplete => Collected.Count >= Seats.Count;
}
