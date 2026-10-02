using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>凭据校验结果：通过时给出服务端推导的身份；拒绝时给出可审计的原因（不含秘密）。</summary>
public readonly record struct CredentialValidation(bool Accepted, ActorKind? Kind, SeatId? Seat, string Reason)
{
    /// <summary>校验通过：身份完全来自服务端持有的凭据记录。</summary>
    public static CredentialValidation Accept(ActorKind kind, SeatId? seat) =>
        new(true, kind, seat, string.Empty);

    /// <summary>校验不通过：原因用于审计日志。</summary>
    public static CredentialValidation Reject(string reason) =>
        new(false, null, null, reason);
}
