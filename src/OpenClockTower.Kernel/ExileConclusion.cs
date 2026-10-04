namespace OpenClockTower.Kernel;

/// <summary>
/// 流放计票的结论（随 <see cref="ExileVoteCountedEvent"/> 进事件流，折叠层不重算）。
/// </summary>
/// <remarks>
/// 依据 R-0044 第 9 条：达线且未被保护 → 目标死亡；未达线 → 目标存活。
/// 死亡保护（怪咖）随 D3 接进同一收口点，届时新增「被保护」分支；在此之前结论只有这两种。
/// </remarks>
public enum ExileConclusion
{
    /// <summary>达线：目标被流放（目标存活时另有配套死亡事件）。</summary>
    Exiled,

    /// <summary>未达线：目标存活。</summary>
    VotesInsufficient,
}
