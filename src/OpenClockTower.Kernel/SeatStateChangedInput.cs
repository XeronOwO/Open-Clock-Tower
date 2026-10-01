namespace OpenClockTower.Kernel;

/// <summary>
/// 某个座位的状态发生了变化（上游输入）。
/// </summary>
/// <remarks>
/// 依据票据第 5 条：挂起请求声明的座位依赖一旦不满足，内核立即自动作废并给出原因。
/// 内核自己不感知全局状态——变化必须作为输入送进来。
/// </remarks>
public sealed record SeatStateChangedInput : StepMachineInput
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>本次观测到的生死；null = 未观测（不参与判定）。</summary>
    public LifeState? Life { get; init; }

    /// <summary>本次观测到的角色；null = 未观测（不参与判定）。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>变化原因（谁的能力 / 哪个效果 / 人工修正）。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方；说书人直接修正等场景可为空。</summary>
    public SeatId? CausedBy { get; init; }
}
