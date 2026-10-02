namespace OpenClockTower.Kernel;

/// <summary>事件触发求值的输入：已经折入本轮新事件的账 + 座次 + 本轮新事件。</summary>
/// <remarks>
/// <see cref="State"/> 与 <see cref="Events"/> 的关系是硬约定：账里**已经**包含这些事件。
/// 触发器据此判断后果是否仍然成立（"这个人现在还是活的吗"），不必自己重放。
/// </remarks>
public sealed record EventTriggerContext
{
    /// <summary>已经折入 <see cref="Events"/> 的状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局完整座次（按座位号升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>本轮**新产生**的事件（业务事件，或上一轮触发产出的事件）。</summary>
    public required IReadOnlyList<GameEvent> Events { get; init; }
}
