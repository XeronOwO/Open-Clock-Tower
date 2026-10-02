namespace OpenClockTower.Kernel;

/// <summary>
/// 一条疯狂要求被撤下：到期（黎明）、来源死亡 / 换角色，或说书人作废。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0021：施加夜的次日白天与紧随其后的夜晚有效，
/// 在下一个黎明到期（<see cref="EffectTerminationKind.NoLongerApplies"/>）；
/// 来源死亡 / 离场立即解除（<see cref="EffectTerminationKind.SourceDied"/> /
/// <see cref="EffectTerminationKind.SourceLostAbility"/>）。
/// 撤下只改要求自身，不动目标的任何玩家维度（疯狂不是引擎状态，R-0003）。
/// </remarks>
public sealed record MadnessRequirementTerminatedEvent : GameEvent
{
    /// <summary>被撤下的要求。</summary>
    public required MadnessRequirementId Id { get; init; }

    /// <summary>撤下原因（分类 + 说明 + 导致方）。</summary>
    public required EffectTermination Termination { get; init; }
}
