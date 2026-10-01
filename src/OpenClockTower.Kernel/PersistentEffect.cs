namespace OpenClockTower.Kernel;

/// <summary>
/// 持续型效果：被持续查询，而不是「施加一次就结束」；带完整归因——谁施加、用哪个能力、作用于谁。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《重要细节》二-3——角色能力在死亡、中毒或醉酒的那一刻立即失去，
/// 「能力产生的所有持续型效果也会终止」；但如果从中毒或醉酒中恢复健康清醒，
/// 「原本**已经生效**的持续型效果也会继续生效」——是继续，不是重新施加。
/// 这两句对"醉酒 / 中毒"的字面冲突按 <c>docs/standard/rulings.md</c> <b>R-0012</b> 处理：
/// 取**挂起**——不生效但不终止，来源恢复后继续生效；只有死亡或换角色才是不可逆终止。
/// </para>
/// <para>
/// 死亡与角色变化是**不可逆的终止**：依据百科《术语汇总》「死亡」——玩家死亡即失去角色能力，
/// 「其角色能力所产生的任何持续性的效果也会立即终止」；依据百科《重要细节》二-7——
/// 复活或角色变化者「立即失去原角色能力，和原角色能力有关的所有持续性效果也会立即终止」。
/// 因此 <see cref="IsTerminated"/> 一旦置位不再清除（见 <see cref="Termination"/>）。
/// </para>
/// </remarks>
public sealed record PersistentEffect
{
    /// <summary>效果标识：同一条效果在事件流里的身份，重放时用它认人。</summary>
    public required EffectId Id { get; init; }

    /// <summary>施加者（产生这条效果的玩家席位）。</summary>
    public required SeatId Source { get; init; }

    /// <summary>产生这条效果的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>作用对象（这条效果落在谁身上）。</summary>
    public required SeatId Target { get; init; }

    /// <summary>
    /// **施加时**来源的角色：能力属于角色，来源换了角色就等于原角色能力消失（《重要细节》二-7）。
    /// </summary>
    /// <remarks>
    /// 这条记录是效果的"出生条件"，让折叠不必依赖"上一次观测到的角色"——
    /// 那样会在来源角色从未被观测过时留下一个静默漏判的盲区（效果该终止却留在账上继续算生效）。
    /// 施加方必须给出它；这就是本项目的"补全初始条件"原则在效果生命周期上的落点。
    /// </remarks>
    public required CharacterId SourceCharacter { get; init; }

    /// <summary>
    /// 终止事实（原因分类 + 说明 + 导致方）；null = 尚未终止。
    /// 由来源状态变化推导，或由说书人显式作废，**不可逆**。
    /// </summary>
    public EffectTermination? Termination { get; init; }

    /// <summary>是否已终止。</summary>
    public bool IsTerminated => Termination is not null;

    /// <summary>
    /// 来源存活且未醉酒、未中毒时效果生效；从醉酒/中毒恢复后继续生效——同一效果，不是重新施加。
    /// </summary>
    /// <param name="sourceState">产生该效果的席位的**当前**状态。</param>
    public bool IsOperative(SeatState sourceState)
    {
        ArgumentNullException.ThrowIfNull(sourceState);
        return IsOperative(sourceState.Life, sourceState.Drunk, sourceState.Poison);
    }

    /// <summary>
    /// 判定"是否生效"只用到来源的生死 / 醉酒 / 中毒三个维度——状态账里它们未必齐全，
    /// 因此单列这个重载：账到哪三个维度就能判到哪一步，不强迫上游先补齐另外两个维度。
    /// </summary>
    /// <param name="life">来源的生死。</param>
    /// <param name="drunk">来源的醉酒状态。</param>
    /// <param name="poison">来源的中毒状态。</param>
    public bool IsOperative(LifeState life, DrunkState drunk, PoisonState poison) =>
        !IsTerminated
        && life == LifeState.Alive
        && drunk == DrunkState.Sober
        && poison == PoisonState.Healthy;

    /// <summary>终止本效果；终止必须带原因。</summary>
    /// <param name="termination">终止原因（分类 + 说明 + 导致方）。</param>
    public PersistentEffect Terminate(EffectTermination termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        return this with { Termination = termination };
    }
}
