namespace OpenClockTower.Kernel;

/// <summary>
/// 常驻效果来源：把「按规则此刻应当存在哪些持续型效果」交给引擎对账（诺-达鲺的中毒、后续的
/// 亡骨魔 / 持续检测型能力都实现这个接口）。
/// </summary>
/// <remarks>
/// <para>
/// 实现只在规则层（<c>OpenClockTower.Rules</c>）：它算期望，不产事件、不碰账；
/// 补什么、终止什么、维度怎么解除由 <see cref="SettlementReconciler"/> 统一决定——
/// 这样"效果 → 维度"的链接只有一个写入方（D-0015）。
/// </para>
/// <para>
/// <see cref="Ability"/> 同时是**管理边界**：对账只终止这个能力名下的效果，
/// 不会误伤别的来源。输入不全时返回判定不了的结论，别猜。
/// </para>
/// </remarks>
public interface IStandingEffectSource
{
    /// <summary>本来源管理的能力。</summary>
    AbilityId Ability { get; }

    /// <summary>算出此刻应当存在的持续型效果；判定不了时显式说明。</summary>
    StandingEffectAssessment Evaluate(StandingEffectContext context);
}
