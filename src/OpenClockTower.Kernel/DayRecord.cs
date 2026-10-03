namespace OpenClockTower.Kernel;

/// <summary>
/// 一天白天的账目：当天的提名、当前「即将被处决」者、实际处决者与结束状态。
/// </summary>
/// <remarks>
/// 依据百科《投票》· 2026-10-01 抓取：计票结束后立即决定玩家是否进入「即将被处决」状态；
/// 即使之后场上状况变化也不再重新判定；提名阶段结束时处决当前「即将被处决」的玩家。
/// </remarks>
public sealed record DayRecord
{
    /// <summary>这是第几天（1 = 首个白天）。</summary>
    public required int DayNumber { get; init; }

    /// <summary>白天是否还在进行。</summary>
    public required DayStatus Status { get; init; }

    /// <summary>当天已发起的提名，按发生顺序。</summary>
    public IReadOnlyList<NominationRecord> Nominations { get; init; } = [];

    /// <summary>
    /// 当天全部投票动作（含撤回与重复动作），按发生顺序——「谁举过手」的原始事实。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="NominationRecord.Ballot"/>（当前票面）分工：票面回答"现在算谁"，动作表回答
    /// "这一天发生过什么"（卖花女孩要读的是后者，且要按动作发生时的角色判定，R-0037）。
    /// </remarks>
    public IReadOnlyList<DayVoteAttempt> VoteAttempts { get; init; } = [];

    /// <summary>
    /// 当前「即将被处决」的玩家；null = 当前没有人（无人提名 / 票数不够 / 最高票平局）。
    /// 只由计票改写（《投票》：计票后不再重判）。
    /// </summary>
    public SeatId? AboutToBeExecuted { get; init; }

    /// <summary>本白天实际被处决的玩家；null = 还没有处决（或本白天以无人被处决收尾）。</summary>
    public SeatId? Executed { get; init; }

    /// <summary>本白天处决的来源分类（常规 / 洗脑师处罚 / 畸形秀演员处罚）；null = 还没有处决。</summary>
    public ExecutionKind? ExecutedKind { get; init; }

    /// <summary>当前投票窗口开着的提名（同一时间至多一项）；没有则为 null。</summary>
    public NominationRecord? OpenNomination =>
        Nominations.LastOrDefault(nomination => nomination.Status == NominationStatus.Voting);

    /// <summary>某个席位今天是否已经发起过提名（每天一次）。</summary>
    public bool HasNominated(SeatId seat) =>
        Nominations.Any(nomination => nomination.Nominator == seat);

    /// <summary>某个席位今天是否已经被提名过（每天一次）。</summary>
    public bool HasBeenNominated(SeatId seat) =>
        Nominations.Any(nomination => nomination.Nominee == seat);
}
