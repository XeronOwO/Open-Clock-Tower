using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一个席位的**邀请凭据**（D-0038）：只存哈希与到期时刻，明文只在签发那一刻存在。
/// </summary>
/// <remarks>
/// <para>
/// 它是"邀请码"在服务端的样子——玩家手上那一串（<c>桌标识:席位邀请码</c>）**不在库里**，
/// 库与备份里只有这条哈希。因此拿到备份的人**没法**直接冒名入座（审计 G-A2-2 的要求）。
/// </para>
/// <para>
/// 一席至多一条：签发即覆盖（轮换），旧的那一枚当场失效；到期即作废，不需要清理任务
/// ——核验时按 <see cref="ExpiresAt"/> 判，过期的行只是垃圾，不影响任何判定。
/// </para>
/// </remarks>
public sealed record SeatInvitation
{
    /// <summary>被邀请的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>邀请码的 SHA-256（存储与比较用；由 <c>SecretToken.HashOf</c> 产出，明文不落地）。</summary>
    public required byte[] Hash { get; init; }

    /// <summary>到期时刻（绝对值，不滑动）。</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
