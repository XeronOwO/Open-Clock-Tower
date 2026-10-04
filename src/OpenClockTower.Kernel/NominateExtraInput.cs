namespace OpenClockTower.Kernel;

/// <summary>窗口授予席位发起一次额外提名（R-0050）；行动者与目标都由服务端从凭据推导。</summary>
public sealed record NominateExtraInput : StepMachineInput
{
    /// <summary>发起提名的席位（必须等于窗口授予席位）。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位（可以是当天已被提名过的玩家）。</summary>
    public required SeatId Nominee { get; init; }
}
