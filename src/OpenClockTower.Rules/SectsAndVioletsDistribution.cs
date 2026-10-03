namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》初始设置的分布表：人数 → 镇民 / 外来者 / 爪牙 / 恶魔。
/// </summary>
/// <remarks>
/// <para>
/// 来源与口径见 <c>docs/standard/rulings.md</c> R-0041：这张表**不在百科页面上**（印在实物
/// 《旅行者列表 / 初始设置表》上），所以逐行标注取证等级与出处——<see cref="SetupRowProvenance.Attested"/>
/// 行有百科逐行出处，<see cref="SetupRowProvenance.Derived"/> 行由百科例证一步推算，
/// <see cref="SetupRowProvenance.Extrapolated"/> 行只由同带结构外推（状态 Open）。改这张表前先读 R-0041。
/// </para>
/// <para>
/// 本类只做数据，不含求解逻辑（求解见 <see cref="SetupComposer"/>）；将来其它剧本共用同一份
/// 人数口径时，在这里扩数据而不是在代码里写分支。
/// </para>
/// </remarks>
public static class SectsAndVioletsDistribution
{
    /// <summary>分布表的一行：人数、计数、取证等级与出处（页名 + 抓取日期）。</summary>
    public sealed record Row(
        int PlayerCount,
        SetupCounts Counts,
        SetupRowProvenance Provenance,
        string Source);

    /// <summary>5–15 人的分布（按人数升序）。</summary>
    public static IReadOnlyList<Row> All { get; } =
    [
        new(5, new SetupCounts(3, 0, 1, 1), SetupRowProvenance.Derived,
            "百科《洗衣妇》· 2026-10-04 抓取（5 人局 + 男爵 → 只剩 1 名镇民 ⇒ 基线 3 镇民 / 0 外来者）"),
        new(6, new SetupCounts(3, 1, 1, 1), SetupRowProvenance.Extrapolated,
            "无直接出处：由 5 人与 7 人（均有出处）之间的同带结构外推（R-0041 状态 Open）"),
        new(7, new SetupCounts(5, 0, 1, 1), SetupRowProvenance.Attested,
            "百科《男爵》· 2026-10-04 抓取（范例：七人游戏初始设置有五名镇民、一名爪牙和一名恶魔）"),
        new(8, new SetupCounts(5, 1, 1, 1), SetupRowProvenance.Attested,
            "百科《梦殒春宵》《暗流涌动》《黯月初升》· 2026-10-04 抓取（八人游戏推荐配置逐角色数得 5 / 1 / 1 / 1）"),
        new(9, new SetupCounts(5, 2, 1, 1), SetupRowProvenance.Derived,
            "百科《异教领袖》· 2026-10-04 抓取（九人游戏七名善良 / 两名邪恶 ⇒ 1 爪牙 + 1 恶魔；外来者按同带结构取 2）"),
        new(10, new SetupCounts(7, 0, 2, 1), SetupRowProvenance.Derived,
            "百科《军团》· 2026-10-04 抓取（十人游戏标准的七名善良 / 三名邪恶 ⇒ 2 爪牙 + 1 恶魔；外来者按同带结构取 0）"),
        new(11, new SetupCounts(7, 1, 2, 1), SetupRowProvenance.Attested,
            "百科《设置调整》· 2026-10-04 抓取（优先级 4 例：11人游戏（7/1/2/1））"),
        new(12, new SetupCounts(7, 2, 2, 1), SetupRowProvenance.Attested,
            "百科《设置调整》/《无名旅客》· 2026-10-04 抓取（12人游戏（7/2/2/1））"),
        new(13, new SetupCounts(9, 0, 3, 1), SetupRowProvenance.Derived,
            "百科《戏法师》· 2026-10-04 抓取（13 人以上的对局多一名爪牙 ⇒ 3 爪牙；外来者按同带结构取 0）"),
        new(14, new SetupCounts(9, 1, 3, 1), SetupRowProvenance.Attested,
            "百科《无名旅客》· 2026-10-04 抓取（14 人对局示例：9 名镇民、1 名外来者、3 名爪牙、1 名恶魔）"),
        new(15, new SetupCounts(9, 2, 3, 1), SetupRowProvenance.Attested,
            "百科《男爵》· 2026-10-04 抓取（范例：十五人游戏初始设置有九名镇民、两名外来者、三名爪牙和一名恶魔）"),
    ];

    /// <summary>人数 → 整行（含取证等级与出处）；不在表内返回 null——不猜（R-0041）。</summary>
    public static Row? RowFor(int playerCount) =>
        All.FirstOrDefault(row => row.PlayerCount == playerCount);

    /// <summary>人数 → 基线分布；不在 5–15 返回 null。</summary>
    public static SetupCounts? BaseFor(int playerCount) => RowFor(playerCount)?.Counts;
}
