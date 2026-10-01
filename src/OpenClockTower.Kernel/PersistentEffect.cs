namespace OpenClockTower.Kernel;

/// <summary>
/// 持续型效果：被持续查询，而不是「施加一次就结束」。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《重要细节》三-3——角色能力在死亡、中毒或醉酒的那一刻立即失去，
/// 「能力产生的所有持续型效果也会终止」；但如果从中毒或醉酒中恢复健康清醒，
/// 「原本**已经生效**的持续型效果也会继续生效」——是继续，不是重新施加。
/// </para>
/// <para>
/// 死亡是**不可逆的终止**：依据百科《术语汇总》「死亡」——玩家死亡即失去角色能力，
/// 「其角色能力所产生的任何持续性的效果也会立即终止」；依据百科《重要细节》二-7——
/// 复活或角色变化者「立即失去原角色能力，和原角色能力有关的所有持续性效果也会立即终止」。
/// 因此 <see cref="IsTerminated"/> 一旦置位不再清除。
/// </para>
/// </remarks>
public sealed record PersistentEffect
{
    /// <summary>效果标识。</summary>
    public required EffectId Id { get; init; }

    /// <summary>产生这条效果的玩家席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>
    /// 来源死亡后置位，**不可逆**：该玩家之后（以新角色）复活，本效果也不会恢复。
    /// </summary>
    public bool IsTerminated { get; init; }

    /// <summary>
    /// 来源存活且未醉酒、未中毒时效果生效；从醉酒/中毒恢复后继续生效——同一效果，不是重新施加。
    /// </summary>
    /// <param name="sourceState">产生该效果的席位的**当前**状态。</param>
    public bool IsOperative(SeatState sourceState) =>
        !IsTerminated
        && sourceState.Life == LifeState.Alive
        && sourceState.Drunk == DrunkState.Sober
        && sourceState.Poison == PoisonState.Healthy;

    /// <summary>来源死亡：终止本效果（依据《术语汇总》「死亡」与《重要细节》二-7）。</summary>
    public PersistentEffect Terminate() => this with { IsTerminated = true };
}
