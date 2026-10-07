using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位绑定（D-0021）：本局「席位 ↔ 账号」的认领关系。
/// </summary>
/// <remarks>
/// 属**会话信息**（与席位邀请凭据同类），不进事件流、不进 <c>GameState</c>；
/// 一席一账号、一账号一席（由存储的唯一索引保证）。
/// </remarks>
public sealed record SeatBinding
{
    /// <summary>所属对局。</summary>
    public required GameId GameId { get; init; }

    /// <summary>被认领的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>认领它的账号。</summary>
    public required AccountId AccountId { get; init; }

    /// <summary>认领时刻（审计用；不参与判定）。</summary>
    public required DateTimeOffset BoundAt { get; init; }
}
