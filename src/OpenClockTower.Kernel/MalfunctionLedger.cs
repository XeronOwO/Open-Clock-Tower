namespace OpenClockTower.Kernel;

/// <summary>
/// 失效账本：记录每一次「能力未正常生效」或受外部干扰（如涡流必假，能力本身仍生效）的分类。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0004：一次结算可并列多条原因（逐条记录、互不顶替）；
/// 数学家的数字**不是** <see cref="Count"/>——全账按玩家去重见 <see cref="CountedSeats"/>，
/// 窗口取值见 <see cref="CountedSeatsSinceDawn"/>。
/// <see cref="Unclassified"/> 是仅剩的待核对清单（咖啡师 / 说书人裁定）。
/// </remarks>
public sealed record MalfunctionLedger
{
    /// <summary>全部失效记录，按发生顺序追加；**不随黎明删除**（R-0004 第 4 条）。</summary>
    public IReadOnlyList<Malfunction> Entries { get; init; } = [];

    /// <summary>
    /// 当前黎明窗口在 <see cref="Entries"/> 里的起点：<c>Entries[SinceDawnStart..]</c> 属于
    /// 「上一个黎明之后」；0 = 还没有黎明（首夜，全账都在窗口内）。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="GameStateMachine"/> 折叠 <see cref="DayStartedEvent"/> 时推进（<see cref="AdvanceDawn"/>）：
    /// 物理提示标记的「移除」在平台上表达为窗口推进，而不是删记录——R-0004 第 4 条要求记录与
    /// 数学家是否在场无关，删除会丢掉审计与重放。
    /// </remarks>
    public int SinceDawnStart { get; init; }

    /// <summary>记录一次失效。</summary>
    public MalfunctionLedger Record(SeatId seat, AbilityId ability, MalfunctionKind kind) =>
        this with
        {
            Entries = [.. Entries, new Malfunction { Seat = seat, Ability = ability, Kind = kind }],
        };

    /// <summary>黎明：把窗口起点推进到当前末尾（不删记录）。</summary>
    public MalfunctionLedger AdvanceDawn() => this with { SinceDawnStart = Entries.Count };

    /// <summary>原始记录条数；**不是数学家的数字**（R-0004 按玩家去重、按窗口取值）。</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// 出现过「计入数学家的」失效的席位（按玩家去重）——R-0004 第 1 条：
    /// 同一名玩家不论多少次、换过几次角色，都只算 1。
    /// </summary>
    /// <remarks>
    /// **不是数学家的数字**：这是全账（不含窗口），也不排除数学家自己的席位；
    /// 数学家口径用 <see cref="CountedSeatsSinceDawn"/>。
    /// </remarks>
    public IReadOnlyList<SeatId> CountedSeats =>
        [.. Entries.Where(entry => entry.Kind.CountsForMathematician())
            .Select(entry => entry.Seat)
            .Distinct()
            .OrderBy(seat => seat.Value)];

    /// <summary>
    /// 「上一个黎明到此刻」窗口内、出现过「计入数学家的」失效的席位（按玩家去重）——R-0004 第 1 / 2 条。
    /// </summary>
    /// <remarks>
    /// 数学家自身不计（R-0004 第 3 条）由取值方排除自己的席位：本查询只做窗口与去重，
    /// 不知道"谁是数学家"。
    /// </remarks>
    public IReadOnlyList<SeatId> CountedSeatsSinceDawn =>
        [.. Entries.Skip(SinceDawnStart)
            .Where(entry => entry.Kind.CountsForMathematician())
            .Select(entry => entry.Seat)
            .Distinct()
            .OrderBy(seat => seat.Value)];

    /// <summary>口径未定（<see cref="MalfunctionKind.Open"/>）的记录——R-0004 仅剩的待核对清单。</summary>
    public IReadOnlyList<Malfunction> Unclassified =>
        [.. Entries.Where(entry => entry.Kind == MalfunctionKind.Open)];
}
