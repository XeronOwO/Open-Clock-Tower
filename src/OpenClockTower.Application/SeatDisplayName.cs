using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位 → 玩家名的公开映射项（D-0021）：姓名是公开呈现信息，允许重名。
/// </summary>
/// <remarks>没有玩家名的席位（游客）不出现——呈现层据此回退「N 号」。</remarks>
public sealed record SeatDisplayName
{
    /// <summary>席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>玩家名（账号当前值；归一化后非空）。</summary>
    public required string DisplayName { get; init; }
}
