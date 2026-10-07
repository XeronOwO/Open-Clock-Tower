using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人**裁定一条离场申请**（本批 D-0037）：批准即执行座位离场，驳回则本局继续。
/// </summary>
/// <remarks>
/// <para>
/// 批准走的是与 <see cref="RemoveTravellerCommand"/> **同一份**离场合法性判定
/// （<c>TravellerCommandDispatch</c>：未离场、有角色、是旅行者、流放与钟盘都未在走），
/// 不另写一份判据——两份判据必然分叉。驳回只结清申请，不碰席位。
/// </para>
/// <para>
/// 说书人也保留**直接移出**的权限（<see cref="RemoveTravellerCommand"/>）：那条路径会顺手把
/// 同一席位待批的申请结清为"批准"，不留下悬空申请。
/// </para>
/// </remarks>
public sealed record ResolveTravellerDepartureCommand : GameCommand
{
    /// <summary>被裁定的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>true = 批准（该席位随即离场）；false = 驳回。</summary>
    public required bool Approved { get; init; }

    /// <summary>说书人给出的说明（驳回原因 / 批准附注；自由文本，可空）。</summary>
    public string? Note { get; init; }
}
