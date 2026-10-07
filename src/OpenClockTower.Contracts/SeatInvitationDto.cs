namespace OpenClockTower.Contracts;

/// <summary>
/// 刚签发的席位邀请码（D-0038）：**明文只出现这一次**——说书人把它转交给玩家，服务端只留哈希。
/// </summary>
/// <remarks>
/// 刷新、重投、重新查询都拿不回它，所以面板必须把它显示清楚并提示"换了就作废"。
/// 与账号会话凭据同一个口径：凭据不进事件流、不进任何玩家投影。
/// </remarks>
public sealed record SeatInvitationDto
{
    /// <summary>被邀请的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>
    /// 完整邀请码（`桌标识:席位邀请码`）——说书人**原样**转交的那一串。
    /// </summary>
    /// <remarks>
    /// 由服务端拼（协议形态只在服务端定义一处）：玩家那一面把它整串粘进「有邀请码？」，
    /// 冒号之前是桌、之后是凭据。
    /// </remarks>
    public required string InviteCode { get; init; }

    /// <summary>到期时刻（面板据此说明"这一枚什么时候作废"）。</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
