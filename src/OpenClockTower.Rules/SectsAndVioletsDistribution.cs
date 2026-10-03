namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》初始设置的分布表：人数 → 镇民 / 外来者 / 爪牙 / 恶魔。
/// </summary>
/// <remarks>
/// <para>
/// 来源与口径见 <c>docs/standard/rulings.md</c> R-0041：这张表**不在百科页面上**（印在实物
/// 《旅行者列表 / 初始设置表》上），所以逐行标注取证等级——<see cref="SetupRowProvenance.Attested"/>
/// 行有百科逐行出处，<see cref="SetupRowProvenance.Extrapolated"/> 行为结构外推、状态 Open。
/// 改这张表前先读 R-0041。
/// </para>
/// <para>
/// 本类只做数据，不含求解逻辑（求解见 <see cref="SetupComposer"/>）；将来其它剧本共用同一份
/// 人数口径时，在这里扩数据而不是在代码里写分支。
/// </para>
/// </remarks>
public static class SectsAndVioletsDistribution
{
    /// <summary>分布表的一行：人数、计数与取证等级。</summary>
    public sealed record Row(int PlayerCount, SetupCounts Counts, SetupRowProvenance Provenance);

    /// <summary>5–15 人的分布（按人数升序）。</summary>
    public static IReadOnlyList<Row> All { get; } =
    [
        // 有据行见 R-0041「依据」；外推行的结构口径见 R-0041「处理」第 1 条。
        new(5, new SetupCounts(3, 0, 1, 1), SetupRowProvenance.Extrapolated),
        new(6, new SetupCounts(3, 1, 1, 1), SetupRowProvenance.Extrapolated),
        new(7, new SetupCounts(5, 0, 1, 1), SetupRowProvenance.Attested), // 百科《男爵》· 范例（2026-10-04 抓取）
        new(8, new SetupCounts(5, 1, 1, 1), SetupRowProvenance.Extrapolated),
        new(9, new SetupCounts(5, 2, 1, 1), SetupRowProvenance.Extrapolated),
        new(10, new SetupCounts(7, 0, 2, 1), SetupRowProvenance.Extrapolated),
        new(11, new SetupCounts(7, 1, 2, 1), SetupRowProvenance.Attested), // 百科《设置调整》· 优先级 4 例（2026-10-04 抓取）
        new(12, new SetupCounts(7, 2, 2, 1), SetupRowProvenance.Attested), // 百科《设置调整》/《无名旅客》（2026-10-04 抓取）
        new(13, new SetupCounts(9, 0, 3, 1), SetupRowProvenance.Extrapolated),
        new(14, new SetupCounts(9, 1, 3, 1), SetupRowProvenance.Attested), // 百科《无名旅客》· 运作方式示例（2026-10-04 抓取）
        new(15, new SetupCounts(9, 2, 3, 1), SetupRowProvenance.Attested), // 百科《男爵》· 范例（2026-10-04 抓取）
    ];

    /// <summary>人数 → 整行（含取证等级）；不在表内返回 null——不猜（R-0041）。</summary>
    public static Row? RowFor(int playerCount) =>
        All.FirstOrDefault(row => row.PlayerCount == playerCount);

    /// <summary>人数 → 基线分布；不在 5–15 返回 null。</summary>
    public static SetupCounts? BaseFor(int playerCount) => RowFor(playerCount)?.Counts;
}
