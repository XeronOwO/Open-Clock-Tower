namespace OpenClockTower.Server;

/// <summary>
/// 结构比对里的一处**偏差**（M5 / G-A6-1）：哪张表 / 哪个索引、差在哪、要不要拦启动。
/// </summary>
/// <param name="Kind">偏差类别。</param>
/// <param name="Target">定位（表名，或 <c>表.列</c> / <c>表.索引</c>）。</param>
/// <param name="Detail">差在哪（人话，进日志与失败信息）。</param>
/// <param name="RepairSql">能自愈时的修复语句；null = 自愈不了。</param>
/// <param name="IsFatal">是否阻断启动。**只有"库里有、模型也要，但形状不对"才是致命的**。</param>
public sealed record SchemaDeviation(
    SchemaDeviationKind Kind,
    string Target,
    string Detail,
    string? RepairSql,
    bool IsFatal)
{
    /// <summary>这一处能不能自己修好。</summary>
    public bool IsRepairable => RepairSql is not null;

    /// <summary>一行人话（日志与失败信息共用同一种表述）。</summary>
    public string Describe() => $"{Kind} {Target}：{Detail}";
}
