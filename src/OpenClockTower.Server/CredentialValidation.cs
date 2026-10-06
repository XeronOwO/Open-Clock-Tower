using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 凭据校验结果：通过时给出服务端推导的身份**与这条连接所在的桌**；拒绝时给出可审计的原因（不含秘密）。
/// </summary>
/// <remarks>
/// 多桌（D-0024）把"在哪一桌"并进凭据记录：一条连接只属于一桌，这是唯一事实来源。
/// </remarks>
public readonly record struct CredentialValidation(
    bool Accepted,
    ActorKind? Kind,
    SeatId? Seat,
    GameId? Game,
    string Reason)
{
    /// <summary>校验通过：身份与所在桌完全来自服务端持有的凭据记录。</summary>
    public static CredentialValidation Accept(ActorKind kind, SeatId? seat, GameId game) =>
        new(true, kind, seat, game, string.Empty);

    /// <summary>校验不通过：原因用于审计日志。</summary>
    public static CredentialValidation Reject(string reason) =>
        new(false, null, null, null, reason);
}
