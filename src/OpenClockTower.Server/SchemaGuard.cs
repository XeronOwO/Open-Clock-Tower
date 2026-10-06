using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OpenClockTower.Server;

/// <summary>
/// 结构核对（M5 / G-A6-1）：迁移跑完之后，把**库里的形状**与**模型的期望**逐项比一遍。
/// </summary>
/// <remarks>
/// <para>
/// 为什么必须有这一步：迁移清单只证明"我们打算做什么"，不证明"库里现在是什么"。
/// 审计咬到的缺口正在这里——旧守卫走的是"表在不在 → 补列 / 删列"，**索引与约束连问都没问**，
/// 而两条唯一索引是"一号一人 / 一席一人"的唯一执行者。丢了它，规则就从"数据库保证"退化成"但愿如此"。
/// </para>
/// <para>
/// 分档见 <see cref="SchemaComparer"/>：索引这类纯增量的差异当场自愈并记一行；
/// 表 / 列 / 类型 / 主键这类差异**拒绝启动**——继续跑只会把数据写进错误的形状里，
/// 那时候再回头，代价就不是一次重启了。
/// </para>
/// <para>
/// 自愈之后**一定要读回**：修没修好不是"我执行过了"说了算（纸面审查 = 没审查）。
/// 读回来还在的偏差，无论属于哪一档，都按致命处理——那说明连自愈这条退路都不成立。
/// </para>
/// </remarks>
public static class SchemaGuard
{
    /// <summary>核对并按档处理；返回自愈了几处。库结构对不上时抛异常（调用方让启动失败）。</summary>
    public static async Task<int> EnforceAsync(GameDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var contract = SchemaContract.FromModel(db);
        var connection = db.Database.GetDbConnection();
        var before = await SqliteSchemaReader.ReadAsync(connection, cancellationToken);
        var deviations = SchemaComparer.Compare(contract, before);

        var repairable = deviations.Where(deviation => deviation.IsRepairable).ToList();
        foreach (var deviation in repairable)
        {
            // 每一处都留一行：自愈是**静默地改变库**的动作，日志里必须能回答"它改了什么"。
            logger.LogWarning(
                "结构自愈：{Target}——{Detail}；执行：{Sql}",
                deviation.Target,
                deviation.Detail,
                deviation.RepairSql);
            await db.Database.ExecuteSqlRawAsync(deviation.RepairSql!, cancellationToken);
        }

        var after = repairable.Count == 0
            ? before
            : await SqliteSchemaReader.ReadAsync(connection, cancellationToken);
        var remaining = SchemaComparer.Compare(contract, after);
        var unresolved = remaining.Where(deviation => deviation.IsFatal || deviation.IsRepairable).ToList();
        if (unresolved.Count > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                unresolved.Select(deviation => $"  · {deviation.Describe()}"));
            logger.LogCritical(
                "库结构与本版模型不一致（迁移没跟上 / 库被手工改过 / 程序与库不是一对）：{Count} 处{NewLine}{Detail}",
                unresolved.Count,
                Environment.NewLine,
                detail);
            throw new InvalidOperationException(
                $"库结构与本版模型不一致，服务拒绝在这个库上继续跑（{unresolved.Count} 处）：{Environment.NewLine}{detail}"
                + $"{Environment.NewLine}怎么办：① 换回与这个库匹配的程序版本；"
                + "② 用匹配的备份恢复（部署文档 §6.3）；"
                + "③ 确认是结构该改而迁移没写，就往 SchemaMigrationCatalog 补一条（部署文档 §6.6）。");
        }

        foreach (var deviation in remaining)
        {
            // "多出来的表 / 列 / 索引"不算错：程序回滚到旧版时就是这副样子，EF 只按列名取值。
            logger.LogWarning("结构差异（保留，不影响取值）：{Deviation}", deviation.Describe());
        }

        logger.LogInformation(
            "结构核对：表={Tables} · 索引={Indexes} · 自愈={Repaired} 处 · 保留差异={Tolerated} 处",
            before.Tables.Count,
            before.Tables.Sum(table => table.Indexes.Count),
            repairable.Count,
            remaining.Count);
        return repairable.Count;
    }
}
