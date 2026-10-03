namespace OpenClockTower.Kernel;

/// <summary>重建一个夜晚行动槽位提示所需的上下文（纯数据，D-0008 无行为对象）。</summary>
/// <remarks>
/// <see cref="Character"/> 是**能力归属角色**——槽位契约的检索键，与结算用同一把
/// （代行槽位上是「被获得角色」，见 R-0036）：避免"结算找得到、重建找不到"。
/// </remarks>
public sealed record SlotPromptRequest
{
    /// <summary>槽位标识（同一计划内稳定；诊断用）。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>能力归属角色（结算契约的检索键）。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>行动者席位。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>本局完整座次（升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>入槽时刻的状态账（已提交账 + 本批已产出事件）。</summary>
    public required GameState State { get; init; }

    /// <summary>
    /// 最近一个白天的账目（首夜 / 还没有白天时为 null）：回溯型信息能力的提示要按它推演。
    /// </summary>
    public DayRecord? LastDay { get; init; }
}
