namespace OpenClockTower.Kernel;

/// <summary>
/// 心上人一次死亡触发的处理结果：能力未生效（醉酒 / 中毒）或说书人未裁定，记一条可归因的跳过。
/// </summary>
/// <remarks>
/// 折进 <see cref="StepMachineState.SweetheartSkips"/>：触发器的幂等依据——有了这一条，
/// 触发器不会为同一名心上人的死亡重复求值（R-0039）。
/// </remarks>
public sealed record SweetheartSkipRecord
{
    /// <summary>以心上人身份死亡的席位。</summary>
    public required SeatId Sweetheart { get; init; }

    /// <summary>跳过原因（人类可读，进审计与说书人视图）。</summary>
    public required string Reason { get; init; }
}
