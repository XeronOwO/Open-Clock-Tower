namespace OpenClockTower.Kernel;

/// <summary>
/// 当天一次提名的账目：谁提名谁、投票窗口状态、票面。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《提名》/《投票》· 2026-10-01 抓取：同日每名玩家只能发起一次提名、
/// 也只能被提名一次；计票以票面快照为准。
/// </para>
/// <para>
/// <see cref="Ballot"/> 在投票窗口期间是**当前投赞成的席位**（按席位号升序，可增可减：
/// 在线窗口内允许改票，口径见 <c>docs/standard/rulings.md</c> R-0017）；
/// 计票后它即最终投票者名单，票数 = 名单长度。
/// </para>
/// </remarks>
public sealed record NominationRecord
{
    /// <summary>当天第几次提名（从 1 起，进事件流后稳定）。</summary>
    public required int Index { get; init; }

    /// <summary>发起提名的席位（只可能是存活玩家）。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位（死亡玩家也可以被提名）。</summary>
    public required SeatId Nominee { get; init; }

    /// <summary>
    /// 提名发生时的提名者角色快照；该维度未观测时为 null（不猜）。
    /// </summary>
    /// <remarks>城镇公告员按它推演「今天有没有爪牙发起提名」；口径见 R-0037。</remarks>
    public CharacterId? NominatorCharacter { get; init; }

    /// <summary>投票窗口状态。</summary>
    public required NominationStatus Status { get; init; }

    /// <summary>当前 / 最终投赞成的席位，按席位号升序。</summary>
    public IReadOnlyList<SeatId> Ballot { get; init; } = [];
}
