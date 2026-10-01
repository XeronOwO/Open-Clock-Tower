namespace OpenClockTower.Kernel;

/// <summary>
/// 失效账本：记录每一次「能力未正常生效」及其原因分类。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0004：口径未闭合，<see cref="Unclassified"/>
/// 就是那张**待核对清单**——它不该长期为空，也不该被隐藏。
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

    /// <summary>失效记录总数（数学家最终要的数字来源）。</summary>
    public int Count => Entries.Count;

    /// <summary>口径未定（<see cref="MalfunctionKind.Open"/>）的记录——R-0004 的待核对清单。</summary>
    public IReadOnlyList<Malfunction> Unclassified =>
        [.. Entries.Where(entry => entry.Kind == MalfunctionKind.Open)];
}
