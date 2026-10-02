namespace OpenClockTower.Contracts;

/// <summary>某个玩家的白天投影：公开事实 + 他现在能做什么（服务端算好，呈现层不判规则）。</summary>
public sealed record PlayerDayDto
{
    /// <summary>公开的当天事实。</summary>
    public required DayViewDto PublicFacts { get; init; }

    /// <summary>现在能不能发起提名。</summary>
    public required bool CanNominate { get; init; }

    /// <summary>现在能不能在当前开放提名上投票。</summary>
    public required bool CanVote { get; init; }

    /// <summary>自己当前是否投了赞成。</summary>
    public required bool Voted { get; init; }

    /// <summary>今天还没被提名过的席位（可提名目标，按席位号升序）。</summary>
    public required int[] Candidates { get; init; }
}
