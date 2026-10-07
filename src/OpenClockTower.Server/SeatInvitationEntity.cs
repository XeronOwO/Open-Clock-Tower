namespace OpenClockTower.Server;

/// <summary>席位邀请凭据表行（D-0038）：一个席位一行，只存邀请码的哈希与到期时刻。</summary>
/// <remarks>主键 <c>(GameId, Seat)</c> 就是"一席至多一条有效邀请"这条口径的执行者。</remarks>
public sealed class SeatInvitationEntity
{
    /// <summary>对局标识。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>席位号。</summary>
    public int Seat { get; set; }

    /// <summary>邀请码的 SHA-256（32 字节；明文不落库）。</summary>
    public byte[] Hash { get; set; } = [];

    /// <summary>到期时刻（绝对，不滑动）。</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
