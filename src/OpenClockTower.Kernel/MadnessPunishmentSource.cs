namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人处罚处决的来源：两名与「疯狂」相关的角色。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：两条来源共用同一条命令面与同一条处决上限账，
/// 但规则依据不同——洗脑师看「要求是否未撤下且来源仍生效」，畸形秀演员看「该席位此刻是否是畸形秀演员」。
/// 具体判定与死亡归因由规则层契约 <see cref="IAdjudicatedExecutionSource"/> 提供，内核不认角色 slug。
/// </remarks>
public enum MadnessPunishmentSource
{
    /// <summary>洗脑师：目标未按疯狂要求行动，说书人处罚处决（R-0021）。</summary>
    Cerenovus = 0,

    /// <summary>畸形秀演员：说书人认为他在疯狂地证明自己是外来者（百科《畸形秀演员》）。</summary>
    Mutant,
}
