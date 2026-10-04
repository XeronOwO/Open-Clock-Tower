using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>玩家发起一次流放提议（发起人由服务端从他的连接凭据推导，命令不带自称身份）。</summary>
public sealed record ProposeExileCommand : GameCommand
{
    /// <summary>被提议流放的席位（必须是在局旅行者）。</summary>
    public required SeatId Target { get; init; }
}
