namespace OpenClockTower.Kernel;

/// <summary>一次提名的来源分类：常规提名 / 屠夫窗口的额外提名（R-0050）。</summary>
/// <remarks>
/// 分类只影响「怎么发起」与「计票落靶的口径」：额外提名不需要票数超过当天此前提名（R-0050 第 4 条）；
/// 票面、公开面、死者票权等其余口径与常规提名完全一致（同一套收票与折叠原语）。
/// </remarks>
public enum NominationKind
{
    /// <summary>常规提名（百科《提名》）。</summary>
    Standard,

    /// <summary>屠夫窗口内的额外提名（百科《屠夫》；R-0050）。</summary>
    Extra,
}
