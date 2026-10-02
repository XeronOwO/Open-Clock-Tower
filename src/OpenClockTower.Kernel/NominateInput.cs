namespace OpenClockTower.Kernel;

/// <summary>某玩家发起一次提名（行动者与目标都由服务端从凭据推导，客户端不声明身份）。</summary>
public sealed record NominateInput : StepMachineInput
{
    /// <summary>发起提名的席位。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位。</summary>
    public required SeatId Nominee { get; init; }
}
