namespace OpenClockTower.Kernel;

/// <summary>
/// 一条即时型效果已生效：谁、用哪个能力、对谁。
/// </summary>
/// <remarks>
/// 即时型效果**不会被撤销**——来源之后死亡、醉酒或中毒都不回滚它（百科《重要细节》二-3）。
/// 因此它只是一条发生过的事实：账里记账，不参与"当前是否生效"的判定。
/// </remarks>
public sealed record InstantaneousEffectAppliedEvent : GameEvent
{
    /// <summary>已生效的即时型效果（含施加者、能力、作用对象）。</summary>
    public required InstantaneousEffect Effect { get; init; }
}
