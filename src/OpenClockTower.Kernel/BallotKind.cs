namespace OpenClockTower.Kernel;

/// <summary>
/// 钟盘上的选票族：提名与流放各是一条收票，共用一台钟盘（D2 实施口径）。
/// </summary>
/// <remarks>
/// 口径见 `docs/backlog/in-progress/traveller-and-exile.md`「D2 实施口径」：同一时刻至多一条
/// **未收完**的收票；收票已收完但未计票的选票不占钟盘（计票可延后）。族只用于定位收票，
/// 不参与任何计票判定——流放不是提名（R-0044 第 1 条）。
/// </remarks>
public enum BallotKind
{
    /// <summary>提名投票（R-0017 目标形态）。</summary>
    Nomination,

    /// <summary>旅行者流放表决（R-0044）。</summary>
    Exile,
}
