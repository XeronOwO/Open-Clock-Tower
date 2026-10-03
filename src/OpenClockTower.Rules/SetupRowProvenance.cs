namespace OpenClockTower.Rules;

/// <summary>分布表一行的取证等级（R-0041）。</summary>
public enum SetupRowProvenance
{
    /// <summary>有百科页面逐行出处（页名 + 抓取日期见 R-0041）。</summary>
    Attested,

    /// <summary>按有据行的结构外推、未经百科逐行核对（R-0041 状态 Open）。</summary>
    Extrapolated,
}
