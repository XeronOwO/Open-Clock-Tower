namespace OpenClockTower.Kernel;

/// <summary>
/// 白天账：跨天保留的白天事实（按天记录 + 死亡玩家已消耗的投票权）。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <see cref="StepMachineState"/> 的一部分（随快照持久化、随事件折叠、跨阶段保留），
/// 理由：提名与投票的事实要能被后续夜晚的信息类能力读取（卖花女孩 / 城镇公告员），
/// 死亡玩家的「死后仅剩一次投票」是整局的次数、不能随白天结束清零。
/// </para>
/// <para>
/// 依据百科《投票》· 2026-10-01 抓取：每名死亡的玩家在他死亡后，只剩最后一次投票机会。
/// 在线口径（票面可改、计票时消耗）见 <c>docs/standard/rulings.md</c> R-0017。
/// </para>
/// </remarks>
public sealed record DayState
{
    /// <summary>已经开始的白天，按天序（含进行中的一天）。</summary>
    public IReadOnlyList<DayRecord> Days { get; init; } = [];

    /// <summary>已消耗掉「死后仅一次」投票权的席位（跨天累计），按消耗顺序。</summary>
    public IReadOnlyList<SeatId> SpentVoteTokens { get; init; } = [];

    /// <summary>进行中的白天；没有白天进行中时为 null。</summary>
    public DayRecord? OpenDay =>
        Days.Count > 0 && Days[^1].Status == DayStatus.Open ? Days[^1] : null;

    /// <summary>还没有开始过任何白天。</summary>
    public static DayState Empty { get; } = new();

    /// <summary>该席位的死后投票权是否已经用完。</summary>
    public bool HasSpentVoteToken(SeatId seat) => SpentVoteTokens.Contains(seat);
}
