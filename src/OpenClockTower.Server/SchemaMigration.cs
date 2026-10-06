namespace OpenClockTower.Server;

/// <summary>
/// 一次**结构演进**（M5 / G-A6-2）：版本号 + 说明 + 是否可逆 + 动作。
/// </summary>
/// <remarks>
/// <para>
/// 版本号就是写进库文件头里的 <c>user_version</c>（见 <see cref="SqliteUserVersion"/>）：
/// SQLite 自己的字段、与 DDL 在**同一个事务**里提交，因此"结构变了但版本没记上"这种半截状态不存在。
/// </para>
/// <para>
/// <b>动作是守卫式的</b>：先看现状再决定做不做（<c>CREATE TABLE IF NOT EXISTS</c>、列在不在）。
/// 于是同一条迁移能同时服务两种库——空库（建齐）与既有库（补齐）——不必分两条路径维护，
/// 老库也就不需要"打标成已应用"这种**声称**：它真的跑过。
/// </para>
/// </remarks>
/// <param name="Version">目标版本号（从 1 起**连续**，由清单自检用例守着）。</param>
/// <param name="Description">这次改了什么（进启动日志，也是发布清单的素材）。</param>
/// <param name="IsIrreversible">
/// 是否**不可逆**。不可逆的那些决定"程序能不能单独回滚"：删列这类动作一旦跑过，
/// 旧程序即使拿回旧包也读不了这个库，只能连库一起回（部署文档 §9.3）。
/// </param>
/// <param name="Apply">动作本身。</param>
public sealed record SchemaMigration(
    int Version,
    string Description,
    bool IsIrreversible,
    Func<SchemaMigrationContext, CancellationToken, Task> Apply);
