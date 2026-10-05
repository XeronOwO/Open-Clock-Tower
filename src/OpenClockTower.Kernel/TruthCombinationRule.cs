namespace OpenClockTower.Kernel;

/// <summary>
/// 一个裁定点上「被选中的候选，其真值必须满足什么组合」的声明（博学者 R-0057 / 涡流 R-0028）。
/// </summary>
/// <remarks>
/// <para>
/// 由**规则层**声明（内核不认角色 slug，D-0008）：说书人端据此显示组合结论、提交时服务端据此核对。
/// 两端用同一条声明，前端不做规则判断（web/AGENTS.md §4：合法选项与信息真假都由服务端算好）。
/// </para>
/// <para>
/// 默认 <see cref="Unspecified"/>：与既有裁定点（洗脑师、麻脸巫婆等）完全一致——没有真值面，
/// 提示里也不带候选真值。
/// </para>
/// </remarks>
public enum TruthCombinationRule
{
    /// <summary>不适用：这不是"挑两条信息"的裁定点（前端不显示真值组合面）。</summary>
    Unspecified = 0,

    /// <summary>
    /// 任意组合都可以：能力**未生效**（醉酒 / 中毒 / 死亡）时两条可以都对、都错，或一对一错
    /// （百科《博学者》· 2026-10-01 抓取 · 角色简介；R-0057 第 5 条）。
    /// </summary>
    AnyCombination,

    /// <summary>必须恰好一真一假：能力**生效**时博学者的两条信息（百科《博学者》· 角色能力）。</summary>
    ExactlyOneTrue,

    /// <summary>
    /// 两条都必须为假：涡流在场时「哪怕他们醉酒或中毒，信息也一定是错误的」
    /// （百科《涡流》· 2026-10-01 抓取 · 角色简介 / 运作方式；R-0028）。
    /// </summary>
    AllFalse,

    /// <summary>
    /// 此刻**判不了**该按哪条口径（能力是否生效的维度没观测齐）：平台不猜，提交时按当时的账
    /// 重新判定；说书人端只把这件事写在提示里，不据此拦下提交（D-0015：不猜）。
    /// </summary>
    Indeterminate,
}
