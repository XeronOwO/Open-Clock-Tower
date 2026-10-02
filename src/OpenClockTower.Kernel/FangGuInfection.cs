namespace OpenClockTower.Kernel;

/// <summary>
/// 方古的「限一次」事实：本局已经完成过一次「外来者变成新的邪恶方古」。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《方古》· 2026-10-01 抓取 · 提示标记「限一次」——放置条件「在方古成功触发能力转化外来者时，
/// 放置在魔典中心」、移除时机「持续至整局游戏结束」；· 运作方式 14——「与『每局游戏限一次』能力不同，
/// 这个标记在接下来的游戏中始终留在那里，即使方古死亡或改变角色」。
/// </para>
/// <para>
/// 因此它是**整局**事实而不是席位效果：落 <see cref="StepMachineState.FangGuInfection"/>，
/// 由 <see cref="StepMachineFolder"/> 在阶段边界继承；重放只折事件。
/// </para>
/// </remarks>
public sealed record FangGuInfection
{
    /// <summary>变成新方古的席位（被侵染的外来者）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>发起侵染的原方古席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>记账说明（进审计与说书人视图）。</summary>
    public required string Note { get; init; }
}
