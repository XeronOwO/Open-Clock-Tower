namespace OpenClockTower.Kernel;

/// <summary>
/// 当天一次提名的账目：谁提名谁、投票窗口状态、票面与钟盘收票状态。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《提名》/《投票》· 2026-10-01 抓取：同日每名玩家只能发起一次提名、
/// 也只能被提名一次；计票以票面快照为准。
/// </para>
/// <para>
/// **两种折叠口径**（R-0017）：<see cref="Sweep"/> 为 null 时是旧形态的"投票开放窗口"，
/// <see cref="Ballot"/> 随每次举手 / 撤回即时增删；<see cref="Sweep"/> 不为 null 时是钟盘收票，
/// <see cref="HandsRaised"/> 记"现在谁举着手"，<see cref="Ballot"/> 只由逐席收票追加冻结结论
/// （先举也算、过时不候），收票完成后即最终投票者名单，票数 = 名单长度。
/// </para>
/// </remarks>
public sealed record NominationRecord
{
    /// <summary>当天第几次提名（从 1 起，进事件流后稳定；常规与额外提名共用一个序号序列）。</summary>
    public required int Index { get; init; }

    /// <summary>提名来源分类：常规提名 / 屠夫窗口的额外提名（R-0050）。</summary>
    public NominationKind Kind { get; init; } = NominationKind.Standard;

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
    /// <remarks>旧形态：投票窗口内的实时票面；钟盘形态：逐席收票的冻结结论（收票完成后即最终名单）。</remarks>
    public IReadOnlyList<SeatId> Ballot { get; init; } = [];

    /// <summary>钟盘形态下"现在举着手"的席位（按席位号升序）；旧形态恒为空。</summary>
    /// <remarks>举手是线下所有人看得见的公开动作（R-0017 第 5 条）：只作公开面呈现，不直接计票。</remarks>
    public IReadOnlyList<SeatId> HandsRaised { get; init; } = [];

    /// <summary>钟盘收票状态；还没点「开始」或旧形态时为 null。</summary>
    public VoteSweepState? Sweep { get; init; }
}
