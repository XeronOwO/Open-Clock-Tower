namespace OpenClockTower.Kernel;

/// <summary>
/// 一条持续型效果被显式终止。
/// </summary>
/// <remarks>
/// <para>
/// 只用于**折叠推导不出来**的终止原因（例如说书人强制作废）。
/// 来源死亡、来源换角色这两种由规则直接推导的终止，在
/// <see cref="GameStateMachine"/> 折叠到来源变化事件时就会发生，**不再额外发这条事件**——
/// 否则同一件事会有两处事实来源。重复终止同一条效果属于事件流损坏，折叠时显式抛错。
/// </para>
/// </remarks>
public sealed record PersistentEffectTerminatedEvent : GameEvent
{
    /// <summary>被终止的效果标识。</summary>
    public required EffectId EffectId { get; init; }

    /// <summary>终止原因（分类 + 说明 + 导致方）。</summary>
    public required EffectTermination Termination { get; init; }
}
