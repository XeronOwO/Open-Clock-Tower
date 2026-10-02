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

    /// <summary>现在能不能在当前开放提名上投票。</summary>
    public required bool CanVote { get; init; }

    /// <summary>自己当前是否投了赞成。</summary>
    public required bool Voted { get; init; }

    /// <summary>今天还没被提名过的席位（可提名目标，按席位号升序）。</summary>
    public required int[] Candidates { get; init; }
}
