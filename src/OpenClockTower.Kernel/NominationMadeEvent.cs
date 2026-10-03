namespace OpenClockTower.Kernel;

/// <summary>一次提名被发起：进入该提名的投票窗口。</summary>
/// <remarks>
/// 发起者与目标是否合法由内核在产出本事件前判定（只有存活者可发起、同日一次、同一时间一项）。
/// 依据百科《提名》· 2026-10-01 抓取。
/// </remarks>
public sealed record NominationMadeEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>当天第几次提名（从 1 起）。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>发起提名的席位。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位。</summary>
    public required SeatId Nominee { get; init; }

    /// <summary>
    /// 提名发生时的提名者角色快照（未观测为 null）。
    /// </summary>
    /// <remarks>
    /// 城镇公告员要读「今天有没有爪牙发起提名」（百科《城镇公告员》· 2026-10-01 抓取 · 角色简介 1），
    /// 而角色会在夜里换人（R-0032）：快照落在事件里，重放 / 重启 / 重建才都答得出**当时**是谁。
    /// 口径见 <c>docs/standard/rulings.md</c> R-0037。
    /// </remarks>
    public CharacterId? NominatorCharacter { get; init; }
}
