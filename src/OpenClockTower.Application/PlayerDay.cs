using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 发给某个玩家的白天投影：公开事实 + "我现在能做什么"。
/// </summary>
/// <remarks>
/// 白天信息是**公开信息**（百科《规则概要》三：提名、投票、处决都在桌面上进行）：
/// <see cref="PublicFacts"/> 只含公开事实，权限位是服务端算好的便利值——呈现层不判规则（web/AGENTS §4）。
/// 字段名刻意避开"玩家的说书人专属数据"禁词表（PlayerProjectionLeakGateTests）。
/// </remarks>
public sealed record PlayerDay
{
    /// <summary>最新一天（进行中或最近结束）的公开事实。</summary>
    public required DayRecord PublicFacts { get; init; }

    /// <summary>现在能不能发起提名（白天开着、没有进行中的提名、存活、今天还没发起过）。</summary>
    public required bool CanNominate { get; init; }

    /// <summary>现在能不能在当前开放提名上投票（白天开着、有开放提名、存活或还有死后票权）。</summary>
    public required bool CanVote { get; init; }

    /// <summary>自己当前是否投了赞成（仅对开放中的提名有意义）。</summary>
    public required bool Voted { get; init; }

    /// <summary>今天还没被提名过的席位（可提名目标，按席位号升序）；服务端算好，前端不猜。</summary>
    public required IReadOnlyList<SeatId> NominationCandidates { get; init; }
}
