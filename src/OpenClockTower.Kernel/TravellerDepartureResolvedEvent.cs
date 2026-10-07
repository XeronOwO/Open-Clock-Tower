namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人**裁定了一条离场申请**：批准（随后产出离场事件）或驳回（本局继续）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="TravellerDepartureRequestedEvent"/> 成对：申请与裁定各一条，
/// 复盘按它们还原"谁什么时候提的、说书人怎么裁的"（D-0037）。
/// </para>
/// <para>
/// 折叠口径（<see cref="GameStateMachine"/>）：把该席位的待批申请从表里移除。没有待批申请却要裁定
/// 属于事件流损坏，折叠时显式失败（D-0014 能力 3）。
/// </para>
/// </remarks>
public sealed record TravellerDepartureResolvedEvent : GameEvent
{
    /// <summary>被裁定的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>是否批准（true = 该席位随后离场）。</summary>
    public required bool Approved { get; init; }

    /// <summary>说书人给出的说明（驳回原因 / 直接移出的注记；可空）。</summary>
    public string? Note { get; init; }
}
