namespace OpenClockTower.Kernel;

/// <summary>一条流放提议被发起：进入该流放的收票窗口（R-0044 第 2 / 3 条）。</summary>
/// <remarks>
/// 发起人与目标是否合法由内核在产出本事件前判定：发起人须在局（含死者）、目标须是在局旅行者、
/// 每名旅行者每个白天至多被提议一次。流放不是提名——不占发起者当日提名额度（R-0044 第 1 条）。
/// </remarks>
public sealed record ExileProposedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>当天第几条流放（从 1 起）。</summary>
    public required int ExileIndex { get; init; }

    /// <summary>发起提议的席位。</summary>
    public required SeatId Proposer { get; init; }

    /// <summary>被提议流放的席位。</summary>
    public required SeatId Target { get; init; }
}
