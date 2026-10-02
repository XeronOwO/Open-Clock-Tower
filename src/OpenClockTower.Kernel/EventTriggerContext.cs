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

    /// <summary>
    /// 步骤机状态（本批业务事件折完后的样子；可能为 null = 阶段还没开始）。
    /// 需要读游戏流程状态的触发器用它（例如呆瓜选择的幂等判断与请求归属，R-0027）。
    /// </summary>
    public StepMachineState? Machine { get; init; }

    /// <summary>本批命令之前白天是否开着（R-0027 判断"白天死亡即时公告"的输入）。</summary>
    public bool DayWasOpen { get; init; }
}
