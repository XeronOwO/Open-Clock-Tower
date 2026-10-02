namespace OpenClockTower.Kernel;

/// <summary>一次处决：某席位被处决（提名流程的常规处决，或说书人的处罚处决）。</summary>
/// <remarks>
/// <para>
/// **处决 ≠ 死亡**（百科《处决》· 2026-10-01 抓取）：说书人可能宣布"被处决但没有死亡"。
/// 本事件记录"被处决"这一事实；死亡结果由配套的 <see cref="SeatStateChangedEvent"/>
/// （Life = Dead）单独记录。已经死亡的席位被处罚处决时只记录本事件，不重复记死亡（R-0020）。
/// </para>
/// <para>
/// 每个白天最多一次（《处决》关于处决；处罚处决计入，详见 <c>docs/standard/rulings.md</c> R-0020）；
/// 无合适对象时白天以无人被处决收尾。夜晚发生的处罚处决不占任何白天的上限，因此
/// <see cref="DayNumber"/> 为 null。
/// </para>
/// </remarks>
public sealed record ExecutedEvent : GameEvent
{
    /// <summary>所属白天；null = 发生在夜晚的处罚处决（不写白天账、不占任何白天的上限，R-0020）。</summary>
    public required int? DayNumber { get; init; }

    /// <summary>被处决的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>处决来源分类：提名流程 / 洗脑师处罚 / 畸形秀演员处罚（R-0020）。</summary>
    public required ExecutionKind Kind { get; init; }

    /// <summary>说书人的执行说明（处罚处决的可选备注，进审计）。</summary>
    public string? Note { get; init; }
}
