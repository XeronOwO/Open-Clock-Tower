namespace OpenClockTower.Kernel;

/// <summary>
/// 控制面到点输入：收流放第 N 席的票（由服务端节拍器按收票时间轴发出，R-0017 目标形态）。
/// </summary>
/// <remarks>时间只在上层（D-0008）：内核只校验"这是不是下一待收席位"，不读时钟。</remarks>
public sealed record CollectExileSeatVoteInput : StepMachineInput
{
    /// <summary>针对当天第几条流放；与当前开放的那一条不一致即拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>本次要收票的席位；必须正好是下一待收席位（顺序由内核校验）。</summary>
    public required SeatId Seat { get; init; }
}
