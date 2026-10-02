namespace OpenClockTower.Application;

/// <summary>
/// 房间健康位：这一局的数据面是否完整（恢复 / 重建失败后进入降级）。
/// </summary>
/// <remarks>
/// <para>
/// 它不是游戏事实，而是**会话态**：事件流与快照是唯一事实来源（D-0010），
/// 恢复失败时房间停在空状态并显式报错（D-0014 能力 3）；如果没有这个位，
/// 说书人视图里的"空账"与"这一局还没观测到任何东西"长得一模一样。
/// </para>
/// <para>
/// 语义（票据 <c>room-health-degradation-flag</c>）：恢复失败 / 重建失败置位并带原因与发生时间；
/// **显式重建成功才清除**；重复失败保留首次发生时间、更新原因。
/// 只说书人视图下发；玩家投影里没有它（D-0012 §4.3）。
/// </para>
/// </remarks>
public sealed record RoomHealth
{
    /// <summary>是否处于降级（数据可能已丢失）。</summary>
    public required bool IsDegraded { get; init; }

    /// <summary>降级原因（人话，含失败动作）；正常时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>首次降级的应用层时刻；正常时为 null。</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>健康：新局或恢复成功后的初始态。</summary>
    public static RoomHealth Healthy { get; } = new() { IsDegraded = false };

    /// <summary>
    /// 置为降级：已降级时保留首次发生时间、只更新原因；未降级时按给定时刻置位。
    /// </summary>
    /// <param name="reason">本次失败原因（人话）。</param>
    /// <param name="at">本次失败的应用层时刻（仅首次置位时记录）。</param>
    public RoomHealth Degrade(string reason, DateTimeOffset at) =>
        IsDegraded
            ? this with { Reason = reason }
            : new RoomHealth { IsDegraded = true, Reason = reason, Since = at };
}
