namespace OpenClockTower.Kernel;

/// <summary>一次处决：当前「即将被处决」的玩家被处决。</summary>
/// <remarks>
/// <para>
/// **处决 ≠ 死亡**（百科《处决》· 2026-10-01 抓取）：说书人可能宣布"被处决但没有死亡"。
/// 本事件记录"被处决"这一事实；死亡结果由配套的 <see cref="SeatStateChangedEvent"/>
/// （Life = Dead）单独记录——本票没有免死角色，默认两者同时产生。
/// </para>
/// <para>
/// 每个白天最多一次（《处决》关于处决）；无合适对象时白天以无人被处决收尾。
/// </para>
/// </remarks>
public sealed record ExecutedEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>被处决的席位。</summary>
    public required SeatId Seat { get; init; }
}
