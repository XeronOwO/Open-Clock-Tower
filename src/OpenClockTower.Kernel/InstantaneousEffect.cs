namespace OpenClockTower.Kernel;

/// <summary>
/// 即时型效果：生效当下改变局面，事后**不因来源失效而回滚**；带完整归因——谁施加、用哪个能力、作用对象是谁。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《重要细节》二-3——角色能力在死亡、中毒或醉酒的那一刻失去，
/// 「同时能力产生的所有持续型效果也会终止」；这句话的另一面是：**已经生效的即时型效果不会被撤销**。
/// </para>
/// <para>
/// 与 <see cref="PersistentEffect"/> 的区别在生命周期：即时型没有「当前是否生效」的问题——它已经发生；
/// 持续型会在来源失效时挂起、来源恢复时继续。
/// </para>
/// </remarks>
public sealed record InstantaneousEffect
{
    /// <summary>效果标识。</summary>
    public required EffectId Id { get; init; }

    /// <summary>施加者（产生这条效果的玩家席位）。</summary>
    public required SeatId Source { get; init; }

    /// <summary>产生这条效果的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>作用对象（这条效果落在谁身上）。</summary>
    public required SeatId Target { get; init; }

    /// <summary>
    /// 已生效的即时型效果不会因来源之后死亡/醉酒/中毒而撤销。
    /// 此查询**恒为 true** 是刻意的：它把「不回滚」写成契约而不是注释。
    /// </summary>
    public bool RemainsInEffect() => true;
}
