using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>控制面到点输入：收第 N 席的票（由服务端节拍器发出，R-0017 目标形态）。</summary>
/// <remarks>与 <see cref="SlotQuotaElapsedCommand"/> 同姿态：只由系统节拍器发出，客户端不可伪造。</remarks>
public sealed record CollectSeatVoteCommand : GameCommand
{
    /// <summary>对当天第几次提名收票；与当前开放的那一项不一致会被拒绝。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>本次要收票的席位；必须正好是下一待收席位。</summary>
    public required SeatId Seat { get; init; }
}
