namespace OpenClockTower.Kernel;

/// <summary>
/// 一条持续型效果被施加：谁、用哪个能力、对谁。
/// </summary>
/// <remarks>
/// 产生方是结算引擎的角色实现（未建）；在它落地之前，说书人可作为上游把这条事实报进来。
/// 效果一旦进事件流就由 <see cref="GameStateMachine"/> 折叠成
/// <see cref="GameState.PersistentEffects"/>，并在来源失效时被自动终止。
/// </remarks>
public sealed record PersistentEffectAppliedEvent : GameEvent
{
    /// <summary>被施加的效果（含施加者、能力、作用对象）。</summary>
    public required PersistentEffect Effect { get; init; }
}
