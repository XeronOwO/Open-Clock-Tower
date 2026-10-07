namespace OpenClockTower.Application;

/// <summary>
/// 一次**桌回收**在库里删掉了什么（M5 / G-A6-5）：逐表行数，外加顺手清掉的残渣。
/// </summary>
/// <remarks>
/// 逐表计数不是好看：它是"删干净了没有"的运行时判据——集成用例与真机读数都拿它当证据，
/// 而不是拿"命令没报错"当证据。残渣（<see cref="OrphanRows"/>）单列一项的理由见
/// <see cref="ITableRetirementStore.PurgeAsync"/>。
/// </remarks>
/// <param name="Events">删掉的事件行。</param>
/// <param name="Snapshots">删掉的快照行。</param>
/// <param name="Receipts">删掉的回执行。</param>
/// <param name="SeatBindings">删掉的席位绑定行。</param>
/// <param name="Games">删掉的会话目录行（0 或 1）。</param>
/// <param name="OrphanRows">顺手清掉的**孤儿行**总数：属于不存在的桌的事件 / 快照 / 回执 / 绑定。</param>
public sealed record TablePurgeResult(
    int Events,
    int Snapshots,
    int Receipts,
    int SeatBindings,
    int Games,
    int OrphanRows)
{
    /// <summary>这一桌一共删掉了多少行（不含孤儿）。</summary>
    public int TotalRows => Events + Snapshots + Receipts + SeatBindings + Games;
}
