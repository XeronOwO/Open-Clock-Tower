namespace OpenClockTower.Kernel;

/// <summary>
/// 「近期活动账」里的一条活动分类（博学者候选事实库的「变化」组读它，R-0057-C）。
/// </summary>
/// <remarks>
/// 只记**账上真的发生了什么**，不做归因推断：席位死亡 / 角色变化 / 阵营变化 / 有人被处决。
/// 分类是规则层写文案时的输入，不是规则判定本身。
/// </remarks>
public enum SeatActivityKind
{
    /// <summary>席位死亡（任何原因；「被处决但没有死亡」不记这一条，见 <see cref="Execution"/>）。</summary>
    Death,

    /// <summary>席位角色发生变化（变化前后的角色都已知才算变化，开局分配不算）。</summary>
    CharacterChange,

    /// <summary>
    /// 开局之后**第一次观测到**该席位的角色（不是"变化"）：平台只能从这一刻起算他持有这个角色
    /// （R-0057-B 第 3 条「首个白天」的起算口径靠它）。
    /// </summary>
    CharacterObserved,

    /// <summary>席位阵营发生变化（变化前后的阵营都已知才算变化）。</summary>
    AlignmentChange,

    /// <summary>有人被处决：**处决 ≠ 死亡**（百科《处决》· 2026-10-01 抓取），两条分别记。</summary>
    Execution,

    /// <summary>
    /// 一次「能力未正常生效」且**计入数学家的数字**的记录（R-0004 的计数口径：中毒 / 醉酒 / 涡流 /
    /// 原因未定）；相克规则、能力自身设定与咖啡师不计入，因此也不进活动账。
    /// </summary>
    Malfunction,
}
