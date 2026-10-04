using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 发给某个玩家的白天投影：公开事实 + 公开生死面 + "我现在能做什么"。
/// </summary>
/// <remarks>
/// 白天信息是**公开信息**（百科《规则概要》三：提名、投票、处决都在桌面上进行）：
/// <see cref="PublicView"/> 只含公开事实，<see cref="Lives"/> / <see cref="Announcements"/>
/// 来自 R-0022 的公开生死面（生命标记与黎明公告的等价物，不含死因），
/// 权限位是服务端算好的便利值——呈现层不判规则（web/AGENTS §4）。
/// 字段名刻意避开"玩家的说书人专属数据"禁词表（PlayerProjectionLeakGateTests）。
/// </remarks>
public sealed record PlayerDay
{
    /// <summary>最新一天（进行中或最近结束）的公开事实。</summary>
    public required DayRecord PublicView { get; init; }

    /// <summary>公开生死面：全体席位对外可见的生死（按席位升序；未观测的席位不出现）。</summary>
    public required IReadOnlyList<PublicLifeEntry> Lives { get; init; }

    /// <summary>本日已公告的生死变化（黎明批次 + 白天即时）：死 = <c>Dead</c>、复活 = <c>Alive</c>。</summary>
    public required IReadOnlyList<PublicLifeEntry> Announcements { get; init; }

    /// <summary>现在能不能发起提名（白天开着、没有进行中的提名、存活、今天还没发起过）。</summary>
    public required bool CanNominate { get; init; }

    /// <summary>现在能不能举手 / 放下（白天开着、收票已开始、本席还没被收票、存活或还有死后票权）。</summary>
    public required bool CanVote { get; init; }

    /// <summary>自己当前的举手状态；本席已被收票时为**冻结结论**（仅对开放中的提名有意义）。</summary>
    public required bool Voted { get; init; }

    /// <summary>本席是否已经被收票（先举也算、过时不候；前端据此锁定举手开关）。</summary>
    public required bool SeatCollected { get; init; }

    /// <summary>当前开放提名的钟盘收票呈现（相位 / 当前席位 / 已收席位 / 剩余时间）；没有收票时为 null。</summary>
    public required VoteSweepView? VoteSweep { get; init; }

    /// <summary>今天还没被提名过的席位（可提名目标，按席位号升序）；服务端算好，前端不猜。</summary>
    public required IReadOnlyList<SeatId> NominationCandidates { get; init; }
}
