namespace OpenClockTower.Rules;

/// <summary>分布表一行的取证等级（R-0041）。</summary>
public enum SetupRowProvenance
{
    /// <summary>有百科逐行出处：该行数量在页面上直接给出，或可从页面文字直接数出（页名 + 抓取日期见 R-0041）。</summary>
    Attested,

    /// <summary>
    /// 有百科例证 + 一步推算：如由「九人游戏…七名善良玩家，只有两名邪恶玩家」推出爪牙 / 恶魔各一名；
    /// 类型拆分按同带有据行的结构（外来者 0 → 1 → 2）。推算过程写在 R-0041 依据里。
    /// </summary>
    Derived,

    /// <summary>仅由同带结构外推，百科没有直接例证（R-0041 状态 Open）。</summary>
    Extrapolated,
}
