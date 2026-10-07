using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 刚签发的席位邀请码（D-0038）：**明文只在这一刻存在**，服务端随即只留哈希。
/// </summary>
/// <remarks>
/// 它只往下发一次（说书人的签发回执 → 面板显示 → 说书人转交玩家）。
/// 刷新、重投、重新查询都拿不回它——这正是"库里没有可用明文凭据"的另一面。
/// </remarks>
public sealed record IssuedSeatInvitation
{
    /// <summary>被邀请的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>邀请码明文（说书人转交给玩家的那一串；不含桌标识）。</summary>
    public required string Code { get; init; }

    /// <summary>到期时刻（面板据此告诉说书人"这一枚什么时候作废"）。</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
