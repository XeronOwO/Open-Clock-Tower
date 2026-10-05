namespace OpenClockTower.Contracts;

/// <summary>某个玩家的白天投影：公开事实 + 公开生死面 + 他现在能做什么（服务端算好，呈现层不判规则）。</summary>
public sealed record PlayerDayDto
{
    /// <summary>
    /// 这份白天投影对应的事件流序号：推送取**读取这份投影时**的序号（读时状态），
    /// 快照取快照序号。客户端只接受序号更大的投影，避免旧推送倒灌覆盖新状态。
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>公开的当天事实（提名 / 票面 / 处决等）。</summary>
    public required DayViewDto PublicView { get; init; }

    /// <summary>公开生死面：全体席位对外可见的生死，按席位号升序（未观测的席位不出现）。</summary>
    public required PlayerLifeDto[] Lives { get; init; }

    /// <summary>
    /// 本日已公告的生死变化（黎明批次 + 白天即时）：<c>State</c> 是变化**之后**的状态
    /// （<c>Dead</c> = 死亡、<c>Alive</c> = 复活）；不含死因（R-0022）。
    /// </summary>
    public required PlayerLifeDto[] Announcements { get; init; }

    /// <summary>现在能不能发起提名。</summary>
    public required bool CanNominate { get; init; }

    /// <summary>现在能不能举手 / 放下（白天开着、收票已开始、本席还没被收票、存活或还有死后票权）。</summary>
    public required bool CanVote { get; init; }

    /// <summary>自己当前的举手状态；本席已被收票时为**冻结结论**。</summary>
    public required bool Voted { get; init; }

    /// <summary>本席是否已经被收票（先举也算、过时不候；前端据此锁定举手开关）。</summary>
    public required bool SeatCollected { get; init; }

    /// <summary>今天还没被提名过的席位（可提名目标，按席位号升序）。</summary>
    public required int[] Candidates { get; init; }

    /// <summary>现在能不能发起流放提议（白天开着、本席在局、当前没有未结清的流放）。</summary>
    public required bool CanProposeExile { get; init; }

    /// <summary>今天还没被提议过流放的在局旅行者席位（可流放目标，按席位号升序）。</summary>
    public required int[] ExileCandidates { get; init; }

    /// <summary>
    /// 现在能不能在流放表决里举手 / 放下（白天开着、流放收票已开始、本席在收票名单里且还没被收票）。
    /// 全体在局玩家含死者（R-0044）；死者不耗投票标记。
    /// </summary>
    public required bool CanVoteExile { get; init; }

    /// <summary>本席在流放表决里的举手状态；本席已被收票时为**冻结结论**。</summary>
    public required bool ExileVoted { get; init; }

    /// <summary>本席是否已经被流放收票（先举也算、过时不候；前端据此锁定举手开关）。</summary>
    public required bool ExileSeatCollected { get; init; }

    /// <summary>现在能不能发起额外提名（屠夫窗口开着且本席是窗口授予席位；R-0050）。</summary>
    public required bool CanNominateExtra { get; init; }

    /// <summary>
    /// 额外提名的可提名席位（窗口授予本席时 = 在局座次全部，含今天已被提名过的人；R-0050）；
    /// 窗口不开或本席不是授予席位时为空。
    /// </summary>
    public required int[] ExtraNominationCandidates { get; init; }

    /// <summary>
    /// 现在能不能公开猜测（本席持有杂耍艺人、今天是这次持有的**首个白天**、且还没猜过；R-0057-B）。
    /// 服务端算好，前端只按它使能输入（web/AGENTS §4）。
    /// </summary>
    public required bool CanMakeJugglerGuesses { get; init; }
}
