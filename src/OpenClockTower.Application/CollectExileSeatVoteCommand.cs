using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>控制面到点：收流放第 N 席的票（由系统节拍器发出，玩家 / 说书人没有入口）。</summary>
public sealed record CollectExileSeatVoteCommand : GameCommand
{
    /// <summary>针对当天第几条流放；与当前开放的那一条不一致会被拒绝。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>本次要收票的席位；必须正好是下一待收席位（顺序由内核校验）。</summary>
    public required SeatId Seat { get; init; }
}
