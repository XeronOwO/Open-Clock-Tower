namespace OpenClockTower.Kernel;

/// <summary>某玩家发起一次流放提议（行动者与目标都由服务端从凭据推导；R-0044 第 2 条）。</summary>
public sealed record ProposeExileInput : StepMachineInput
{
    /// <summary>发起提议的席位（在局玩家均可，含死者）。</summary>
    public required SeatId Proposer { get; init; }

    /// <summary>被提议流放的席位（须是在局旅行者；生死不限）。</summary>
    public required SeatId Target { get; init; }
}
