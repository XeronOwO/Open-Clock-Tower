namespace OpenClockTower.Kernel;

/// <summary>
/// 近期活动账：按发生顺序记下「谁死了、谁换了角色 / 阵营、谁被处决」，并维护
/// **最近一个已结束的夜晚**与**当前白天**两个窗口（R-0057-C 的 `last-night` / `today` 口径）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么单独有一本账：状态账（<see cref="GameState"/>）只回答"此刻是什么"，回答不了
/// "昨晚发生了什么"——而"昨晚变化"正是说书人给博学者信息时最常用的一类事实。
/// 这本账与状态账同源、同姿态：由 <see cref="GameStateMachine"/> 折叠事件流得到，
/// **纯函数、无时间无随机**（D-0008），重放必得同一本账。
/// </para>
/// <para>
/// 窗口口径（<c>docs/standard/rulings.md</c> R-0057-C）：夜晚窗口 = 最近一个**已结束**的夜晚阶段
/// （从开夜到黎明），白天窗口 = 从最近一次黎明到此刻。正在进行的夜晚不计入任何已结束窗口；
/// 还没有过黎明时白天窗口从账的开头算起。
/// </para>
/// <para>
/// 记录只增不删（与失效账本同姿态：审计与复算优先，窗口靠下标推进而不是删记录）。
/// </para>
/// </remarks>
public sealed record SeatActivityLedger
{
    /// <summary>全部活动，按发生顺序追加。</summary>
    public IReadOnlyList<SeatActivity> Entries { get; init; } = [];

    /// <summary>最近一个已结束夜晚在 <see cref="Entries"/> 里的起点；null = 还没有已结束的夜晚。</summary>
    public int? LastNightStart { get; init; }

    /// <summary>最近一个已结束夜晚的终点（= 那个黎明的下标，不含）。</summary>
    public int? LastNightEnd { get; init; }

    /// <summary>当前白天窗口的起点：<c>Entries[SinceDawnStart..]</c> 属于「最近一次黎明之后」。</summary>
    public int SinceDawnStart { get; init; }

    /// <summary>正在进行的夜晚的起点（开夜时记）；null = 现在不是夜晚。</summary>
    public int? CurrentNightStart { get; init; }

    /// <summary>记录一条活动。</summary>
    public SeatActivityLedger Record(SeatActivityKind kind, SeatId seat, string? reason) =>
        this with
        {
            Entries = [.. Entries, new SeatActivity { Kind = kind, Seat = seat, Reason = reason }],
        };

    /// <summary>开夜：把「正在进行的夜晚」起点推到当前末尾（夜晚还没结束，不影响已结束窗口）。</summary>
    public SeatActivityLedger StartNight() => this with { CurrentNightStart = Entries.Count };

    /// <summary>
    /// 黎明：把这个夜晚收成一个**已结束的夜晚**，并把白天窗口起点推到当前末尾。
    /// 没有正在进行的夜晚（事件流里缺开夜事实）时不改夜晚窗口——"最近一个已结束的夜晚"
    /// 仍是更早那一个，而不是把白天也算进夜晚。
    /// </summary>
    public SeatActivityLedger StartDay() =>
        CurrentNightStart is { } nightStart
            ? this with
            {
                LastNightStart = nightStart,
                LastNightEnd = Entries.Count,
                SinceDawnStart = Entries.Count,
                CurrentNightStart = null,
            }
            : this with { SinceDawnStart = Entries.Count };

    /// <summary>最近一个已结束夜晚窗口内的活动（没有已结束的夜晚 → 空）。</summary>
    public IReadOnlyList<SeatActivity> LastNight => Window(LastNightStart, LastNightEnd);

    /// <summary>当前白天窗口内的活动（从最近一次黎明到此刻）。</summary>
    public IReadOnlyList<SeatActivity> Today => [.. Entries.Skip(SinceDawnStart)];

    /// <summary>最近一个已结束夜晚里是否出现过某类活动。</summary>
    public bool AnyLastNight(SeatActivityKind kind) => LastNight.Any(entry => entry.Kind == kind);

    /// <summary>当前白天里是否出现过某类活动。</summary>
    public bool AnyToday(SeatActivityKind kind) => Today.Any(entry => entry.Kind == kind);

    private IReadOnlyList<SeatActivity> Window(int? start, int? end) =>
        start is { } from && end is { } to
            ? [.. Entries.Skip(from).Take(to - from)]
            : [];
}
