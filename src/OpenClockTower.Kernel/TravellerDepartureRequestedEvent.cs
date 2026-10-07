namespace OpenClockTower.Kernel;

/// <summary>
/// 一名旅行者**向说书人提出离场申请**：申请进事件流，等说书人裁定（本批 D-0037）。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《旅行者》（2026-10-04 抓取 · 旅行者运作方式）：旅行者的加入与离开由说书人主持；
/// 平台把"谁发起"收成**玩家发起 → 说书人裁定**（`rulings.md` R-0044 第 6 条的离场口径不变，
/// 只把发起人从说书人改成旅行者本人）。
/// </para>
/// <para>
/// 与 <see cref="TravellerDepartedEvent"/> 的分工：本事件只是**申请**——它不改变任何席位账，
/// 说书人裁定（<see cref="TravellerDepartureResolvedEvent"/>）批准时才产出离场事件。
/// </para>
/// <para>
/// 折叠口径（<see cref="GameStateMachine"/>）：把申请登记进状态账的待批表；同一席位同时只能有一条。
/// </para>
/// </remarks>
public sealed record TravellerDepartureRequestedEvent : GameEvent
{
    /// <summary>申请离场的席位（行动者由连接凭据推导，命令面无自称身份）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>旅行者给出的理由（自由文本；可空）。</summary>
    public string? Note { get; init; }
}
