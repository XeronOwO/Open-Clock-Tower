namespace OpenClockTower.Kernel;

/// <summary>
/// 失效账本：记录每一次「能力未正常生效」或受外部干扰（如涡流必假，能力本身仍生效）的分类。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0004：一次结算可并列多条原因（逐条记录、互不顶替）；
/// 数学家的数字**不是** <see cref="Count"/>——按玩家去重见 <see cref="CountedSeats"/>。
/// <see cref="Unclassified"/> 是仅剩的待核对清单（咖啡师 / 说书人裁定）。
/// </remarks>
public sealed record MalfunctionLedger
{
    /// <summary>全部失效记录，按发生顺序追加。</summary>
    public IReadOnlyList<Malfunction> Entries { get; init; } = [];

    /// <summary>记录一次失效。</summary>
    public MalfunctionLedger Record(SeatId seat, AbilityId ability, MalfunctionKind kind) =>
        this with
        {
            Entries = [.. Entries, new Malfunction { Seat = seat, Ability = ability, Kind = kind }],
        };

    /// <summary>原始记录条数；**不是数学家的数字**（R-0004 按玩家去重、按窗口取值）。</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// 出现过「计入数学家的」失效的席位（按玩家去重）——R-0004 第 1 条：
    /// 同一名玩家不论多少次、换过几次角色，都只算 1。
    /// </summary>
    /// <remarks>
    /// **不是数学家的数字**：这里只做全账去重——不含「上一个黎明到被唤醒」的窗口，也不排除数学家自己的席位
    /// （R-0004 第 2 / 3 条）。这两条随数学家角色实现；接线前不得把它直接当数字用。
    /// </remarks>
    public IReadOnlyList<SeatId> CountedSeats =>
        [.. Entries.Where(entry => entry.Kind.CountsForMathematician())
            .Select(entry => entry.Seat)
            .Distinct()
            .OrderBy(seat => seat.Value)];

    /// <summary>口径未定（<see cref="MalfunctionKind.Open"/>）的记录——R-0004 仅剩的待核对清单。</summary>
    public IReadOnlyList<Malfunction> Unclassified =>
        [.. Entries.Where(entry => entry.Kind == MalfunctionKind.Open)];
}
