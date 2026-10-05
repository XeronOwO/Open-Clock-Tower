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

    /// <summary>当天已发起的流放提议，按发生顺序（D2；同日可多次、顺序进行）。</summary>
    public IReadOnlyList<ExileRecord> Exiles { get; init; } = [];

    /// <summary>当天说书人裁定的死亡保护，按裁定顺序（每席位至多一条；R-0048）。</summary>
    public IReadOnlyList<DayProtectionDecision> ProtectionDecisions { get; init; } = [];

    /// <summary>
    /// 当天杂耍艺人的公开猜测，按发生顺序（R-0057-B）：这是**公开事实**——所有玩家都听到了那几句话，
    /// 因此随白天公开面一起下发；猜对数不在这里（当晚由说书人给出，只到本人）。
    /// </summary>
    public IReadOnlyList<JugglerGuessRecord> JugglerGuesses { get; init; } = [];

    /// <summary>
    /// 当前「即将被处决」的玩家；null = 当前没有人（无人提名 / 票数不够 / 最高票平局）。
    /// 只由计票改写（《投票》：计票后不再重判）。
    /// </summary>
    public SeatId? AboutToBeExecuted { get; init; }

    /// <summary>
    /// 本白天实际发生的处决，按发生顺序（常规至多一次；屠夫窗口用掉后可以再有第二次，R-0050）。
    /// </summary>
    /// <remarks>处决 ≠ 死亡：被处决但未死亡（保护 / 已死亡目标）也进这里（R-0020 / R-0048）。</remarks>
    public IReadOnlyList<DayExecution> Executions { get; init; } = [];

    /// <summary>本白天第一次处决的席位；null = 还没有处决。派生自 <see cref="Executions"/>（读取面兼容）。</summary>
    public SeatId? Executed => Executions.Count == 0 ? null : Executions[0].Seat;

    /// <summary>本白天第一次处决的来源分类；null = 还没有处决。派生自 <see cref="Executions"/>。</summary>
    public ExecutionKind? ExecutedKind => Executions.Count == 0 ? null : Executions[0].Kind;

    /// <summary>本白天是否已经发生过处决（含被处决但未死亡；R-0020 / R-0050）。</summary>
    public bool HasExecution => Executions.Count > 0;

    /// <summary>当天打开的额外提名窗口；null = 从未打开（R-0050）。</summary>
    public ExtraNominationWindow? ExtraNomination { get; init; }

    /// <summary>当前投票窗口开着的提名（同一时间至多一项）；没有则为 null。</summary>
    public NominationRecord? OpenNomination =>
        Nominations.LastOrDefault(nomination => nomination.Status == NominationStatus.Voting);

    /// <summary>当前未结清的流放（同一时间至多一条）；没有则为 null。</summary>
    public ExileRecord? OpenExile =>
        Exiles.LastOrDefault(exile => exile.Status == ExileStatus.Voting);

    /// <summary>
    /// 钟盘上正在收票的那一条（唯一「未收完」的收票；提名 / 流放共用一个读取口）。
    /// </summary>
    /// <remarks>
    /// 口径见票据「D2 实施口径」：钟盘 =「未收完的那一条收票」；收票已收完但未计票的选票不占钟盘。
    /// 开始 / 继续收票的冲突判定与控制面节拍器都读这里，避免两处各写一套。
    /// </remarks>
    public ActiveBallot? ActiveBallot
    {
        get
        {
            if (OpenNomination is { Sweep: { IsComplete: false } nominationSweep } nomination)
            {
                return new ActiveBallot
                {
                    Kind = BallotKind.Nomination,
                    Index = nomination.Index,
                    Sweep = nominationSweep,
                };
            }

            if (OpenExile is { Sweep: { IsComplete: false } exileSweep } exile)
            {
                return new ActiveBallot
                {
                    Kind = BallotKind.Exile,
                    Index = exile.Index,
                    Sweep = exileSweep,
                };
            }

            return null;
        }
    }

    /// <summary>某个席位今天是否已经发起过提名（每天一次）。</summary>
    public bool HasNominated(SeatId seat) =>
        Nominations.Any(nomination => nomination.Nominator == seat);

    /// <summary>某个席位今天是否已经被提名过（每天一次）。</summary>
    public bool HasBeenNominated(SeatId seat) =>
        Nominations.Any(nomination => nomination.Nominee == seat);

    /// <summary>某个旅行者今天是否已经被提议过流放（每天一次，成败都算；R-0044 第 3 条）。</summary>
    public bool HasExileProposed(SeatId seat) =>
        Exiles.Any(exile => exile.Target == seat);

    /// <summary>该席位今天已经裁定过的死亡保护；还没有裁定时为 null（R-0048）。</summary>
    public DayProtectionDecision? ProtectionDecisionFor(SeatId seat) =>
        ProtectionDecisions.FirstOrDefault(decision => decision.Seat == seat);
}
