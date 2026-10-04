using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 玩家发起屠夫窗口的额外提名（百科《屠夫》；R-0050）：窗口开着且发起人是窗口授予席位时才受理。
/// </summary>
/// <remarks>
/// 提名者由连接凭据推导，命令面无自称身份；不占当日提名次数、可提名当天已被提名过的玩家（《屠夫》明文）。
/// 受理条件与拒绝码由内核给出（<c>docs/standard/rulings.md</c> R-0050）。
/// </remarks>
public sealed record NominateExtraCommand : GameCommand
{
    /// <summary>被提名的席位（可以是当天已被提名过的玩家）。</summary>
    public required SeatId Nominee { get; init; }
}
