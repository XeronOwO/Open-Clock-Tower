using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>一名玩家的会话票据（占位版：票据流程的加固属零信任票据）。</summary>
public sealed record SeatTicket
{
    /// <summary>席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>票据明文（服务端持久化；重连时重新出示）。</summary>
    public required string Ticket { get; init; }
}
