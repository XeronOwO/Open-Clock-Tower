using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>玩家发起一次提名（提名者由服务端从他的连接凭据推导，命令不带自称身份）。</summary>
public sealed record NominateCommand : GameCommand
{
    /// <summary>被提名的席位。</summary>
    public required SeatId Nominee { get; init; }
}
