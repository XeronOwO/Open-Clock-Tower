namespace OpenClockTower.Kernel;

/// <summary>
/// 流放计票的结论（随 <see cref="ExileVoteCountedEvent"/> 进事件流，折叠层不重算）。
/// </summary>
/// <remarks>
/// 依据 R-0044 第 9 条：达线且未被保护 → 目标死亡；未达线 → 目标存活；受保护 → 目标存活且不产生死亡
/// （R-0048：保护不拦流放流程本身，票照收、数照记）。三种结论各自对应一种失败 / 成功原因。
/// </remarks>
public enum ExileConclusion
{
    /// <summary>达线：目标被流放（目标存活且未被保护时另有配套死亡事件）。</summary>
    Exiled,

    /// <summary>未达线：目标存活。</summary>
    VotesInsufficient,

    /// <summary>达线但受死亡保护：目标存活，不产生死亡事件（R-0048）。</summary>
    Protected,
}
