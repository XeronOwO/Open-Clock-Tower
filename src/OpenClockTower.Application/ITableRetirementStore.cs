namespace OpenClockTower.Application;

/// <summary>
/// 桌的**退役端口**（M5 / G-A6-5）：一张桌的活跃度读数，以及把它从库里彻底删掉的那一个出口。
/// </summary>
/// <remarks>
/// <para>
/// 审计的原话是"库层没有任何删除路径"：<c>IAccountStore</c> / <c>IGameStore</c> / <c>IGameCatalog</c>
/// 一个删除方法都没有，而一张桌在库里横跨五张表（Events / Snapshots / Receipts / Games / SeatBindings），
/// 于是"清掉一张没人玩的桌"只能由运维停服、手工五表联删（部署文档 §9.4 的旧写法）。
/// </para>
/// <para>
/// 读数与删除放在同一个端口里，是因为它们只服务同一个用例——"这张桌该不该退役"：
/// 判定要的事实（建桌时刻 / 最后事件 / 最后绑定）与删除动作必须看同一份库状态，
/// 分成两个端口只会让调用方以为可以"先读后删"而中间隔着无限久。
/// </para>
/// </remarks>
public interface ITableRetirementStore
{
    /// <summary>
    /// 列出**每一桌**的活跃度读数（按桌标识升序）。
    /// </summary>
    /// <remarks>
    /// 一次查询给全量，**不是逐桌查**：大厅列表那条 N+1（审计 G-A5-5）正是逐桌查询长出来的，
    /// 没理由在回收这条路上再写一遍。
    /// </remarks>
    Task<IReadOnlyList<TableActivity>> ListActivityAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 把一张桌从库里删干净：五张表里属于它的行，**一个事务**删完，并顺手清掉孤儿行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 事务是必须的：删到一半（比如目录行没了、事件还在）会得到一张"看得见却打不开"的桌。
    /// </para>
    /// <para>
    /// **顺手清孤儿**这一半也是删除路径的一部分：属于不存在的桌的事件 / 快照 / 回执 / 绑定
    /// 在任何时候都是残渣（历史上有过"运维手工五表联删"的年代，漏一张表就会留下它们），
    /// 而残渣没有别的清理入口——只有这里知道"哪些桌是存在的"。清出来多少记在
    /// <see cref="TablePurgeResult.OrphanRows"/> 里，不静默。
    /// </para>
    /// <para>
    /// 删除是**不可逆**的：没有回收站、没有软删标记（软删 = 数据还在库里，
    /// 那会让隐私说明里的"留多久"变成一句空话，也会让每个读路径都要记得过滤）。
    /// 后悔药是每天那份备份与它保留的若干份（M5 / G-A6-4）。
    /// </para>
    /// </remarks>
    Task<TablePurgeResult> PurgeAsync(GameId gameId, CancellationToken cancellationToken);
}
