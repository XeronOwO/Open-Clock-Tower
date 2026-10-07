namespace OpenClockTower.Application;

/// <summary>
/// 一张桌的**活跃度读数**（M5 / G-A6-5）：判定它还算不算"有人在用"所要的全部事实。
/// </summary>
/// <remarks>
/// <para>
/// 这里刻意**不缓存任何"最后活跃时刻"**：活跃度是 <see cref="CreatedAt"/>、
/// <see cref="LastEventAt"/>、<see cref="LastBindingAt"/> 三者的最大值，三样都是库里已有的事实
/// （建桌时刻、最后一条事件、最后一次认领席位）。多存一个"最后活跃"列就等于立第二份事实，
/// 而两份事实必然分叉——分叉的那一天，"这张桌多久没人动了"就没有答案了。
/// </para>
/// <para>
/// 三个时刻**都可能是 null**：老库（v2 迁移之前建的）没有建桌时刻，从未开局的桌没有事件，
/// 没人坐过的桌没有绑定。三者全空 = **没有依据判定它空闲了多久**，
/// 那种桌一律不回收（见 <c>TableRetirementService</c>）。
/// </para>
/// </remarks>
/// <param name="GameId">桌标识。</param>
/// <param name="Name">桌名（只为报告与日志好认；判定不看它）。</param>
/// <param name="CreatedAt">建桌时刻（v2 起由会话目录写入；老库由迁移按首条事件回填，无事件则为 null）。</param>
/// <param name="EventCount">事件条数（0 = 从未开局）。</param>
/// <param name="LastEventAt">最后一条事件的记录时刻。</param>
/// <param name="LastBindingAt">最后一次席位认领的时刻。</param>
/// <param name="PayloadBytes">这一桌事件载荷的字节数（容量读数用，判定不看它）。</param>
public sealed record TableActivity(
    GameId GameId,
    string Name,
    DateTimeOffset? CreatedAt,
    int EventCount,
    DateTimeOffset? LastEventAt,
    DateTimeOffset? LastBindingAt,
    long PayloadBytes)
{
    /// <summary>这张桌是否开过局（产生过事件）。</summary>
    public bool HasStarted => EventCount > 0;

    /// <summary>
    /// 最后一次有据可查的活跃时刻；三者全空时为 null（= **无法判定**，不是"很久没动"）。
    /// </summary>
    public DateTimeOffset? LastActivityAt
    {
        get
        {
            DateTimeOffset? latest = null;
            foreach (var candidate in new[] { CreatedAt, LastEventAt, LastBindingAt })
            {
                if (candidate is { } value && (latest is null || value > latest))
                {
                    latest = value;
                }
            }

            return latest;
        }
    }
}
