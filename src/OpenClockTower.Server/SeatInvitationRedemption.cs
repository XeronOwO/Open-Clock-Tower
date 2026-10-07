using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 邀请码的核验结果：通过时给出它指向的**席位**；拒绝时给出可审计的原因（不含秘密）。
/// </summary>
/// <remarks>
/// "这一桌没有这枚码"与"这一枚过期了"刻意分开：前者不该泄露任何东西，后者要能让玩家看懂
/// "去再要一个"——而说这话的人本来就拿着那枚码，区分它不增加任何人的信息量。
/// </remarks>
public readonly record struct SeatInvitationRedemption(bool Accepted, SeatId? Seat, string Reason)
{
    /// <summary>这一桌没有这枚邀请码。</summary>
    public const string UnknownReason = "invitation.unknown";

    /// <summary>这一枚邀请码已经过期。</summary>
    public const string ExpiredReason = "invitation.expired";

    /// <summary>核验通过：这就是它指向的席位。</summary>
    public static SeatInvitationRedemption Accept(SeatId seat) => new(true, seat, string.Empty);

    /// <summary>核验不通过：原因用于审计日志与面向玩家的文案。</summary>
    public static SeatInvitationRedemption Reject(string reason) => new(false, null, reason);
}
