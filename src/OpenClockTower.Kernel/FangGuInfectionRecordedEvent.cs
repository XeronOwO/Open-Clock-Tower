namespace OpenClockTower.Kernel;

/// <summary>
/// 方古的「限一次」标记落下：本局首次成功把外来者变成新的邪恶方古。
/// </summary>
/// <remarks>
/// 依据：百科《方古》· 2026-10-01 抓取 · 角色简介 2（「方古首次攻击并成功杀死外来者时，改为方古死亡，
/// 外来者变成邪恶的方古」）与提示标记「限一次」（放置于魔典中心、持续至整局结束）。
/// 折叠成 <see cref="StepMachineState.FangGuInfection"/>：标记整局不复用，即使原方古死亡 / 换角、
/// 或之后由其他角色制造出新的方古（运作方式 14）。
/// </remarks>
public sealed record FangGuInfectionRecordedEvent : GameEvent
{
    /// <summary>变成新方古的席位（被侵染的外来者）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>发起侵染的原方古席位。</summary>
    public required SeatId Source { get; init; }
}
