namespace OpenClockTower.Server;

/// <summary>
/// 把"期望结构"与"库里实际的结构"逐项比一遍（M5 / G-A6-1），差在哪、能不能自愈、要不要拦启动。
/// </summary>
/// <remarks>
/// <para>
/// **纯计算**：不碰数据库、不打日志。这样它可以被几十行用例完整覆盖，
/// 而"真的要动库"的那一半（自愈、报错退出）留在 <see cref="SchemaGuard"/> 里。
/// </para>
/// <para>
/// 三档结论，分档的依据是风险：
/// </para>
/// <list type="number">
/// <item><b>可自愈</b>：缺索引 / 索引形状不符——索引里没有数据，重建是安全的，而**丢掉唯一索引**
/// 会让"一号一人 / 一席一人"变成纸面规定（那两条唯一索引是它们唯一的执行者）。</item>
/// <item><b>致命</b>：缺表 / 缺列 / 类型或 NOT NULL 不符 / 主键不符——这些要么装不下数据、
/// 要么装错数据，只能报出来让人决定（升级程序、恢复备份、或补一条迁移）。</item>
/// <item><b>不算错</b>：库里多出来的表 / 列 / 索引——**程序回滚到旧版时就是这副样子**，
/// 例外是"多出来的列还是 <c>NOT NULL</c> 且没有默认值"（见 <see cref="SchemaDeviationKind.UnexpectedColumn"/>）。</item>
/// </list>
/// </remarks>
public static class SchemaComparer
{
    /// <summary>比一遍；返回**稳定排序**的偏差清单（空 = 库结构与模型一致）。</summary>
    public static IReadOnlyList<SchemaDeviation> Compare(DatabaseSchema expected, DatabaseSchema actual)
    {
        var deviations = new List<SchemaDeviation>();

        foreach (var table in expected.Tables)
        {
            var live = actual.TableNamed(table.Name);
            if (live is null)
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.MissingTable,
                    table.Name,
                    "库里没有这张表（迁移漏了？）",
                    null,
                    IsFatal: true));
                continue;
            }

            CompareColumns(table, live, deviations);
            CompareIndexes(table, live, deviations);
        }

        foreach (var table in actual.Tables.Where(table => !expected.HasTable(table.Name)))
        {
            deviations.Add(new SchemaDeviation(
                SchemaDeviationKind.UnexpectedTable,
                table.Name,
                "模型里没有这张表（上一版留下的？）",
                null,
                IsFatal: false));
        }

        return
        [
            .. deviations
                .OrderBy(deviation => deviation.Target, StringComparer.Ordinal)
                .ThenBy(deviation => deviation.Kind),
        ];
    }

    /// <summary>逐列比：缺列 / 类型 / NOT NULL，外加"多出来的列会不会把 INSERT 拒掉"。</summary>
    private static void CompareColumns(
        DatabaseSchema.Table expected,
        DatabaseSchema.Table actual,
        List<SchemaDeviation> deviations)
    {
        foreach (var column in expected.Columns)
        {
            var live = actual.ColumnNamed(column.Name);
            if (live is null)
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.MissingColumn,
                    $"{expected.Name}.{column.Name}",
                    $"库里没有这一列（模型要 {column.DeclaredType}{(column.NotNull ? " NOT NULL" : string.Empty)}）",
                    null,
                    IsFatal: true));
                continue;
            }

            if (!string.Equals(column.DeclaredType, live.DeclaredType, StringComparison.OrdinalIgnoreCase))
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.ColumnTypeMismatch,
                    $"{expected.Name}.{column.Name}",
                    $"声明类型不符：库里是 {Describe(live.DeclaredType)}，模型要 {Describe(column.DeclaredType)}",
                    null,
                    IsFatal: true));
            }

            if (column.NotNull != live.NotNull)
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.ColumnNullabilityMismatch,
                    $"{expected.Name}.{column.Name}",
                    column.NotNull
                        ? "模型要求 NOT NULL，库里这一列可以为空"
                        : "模型允许为空，库里这一列是 NOT NULL（写入空值会被拒）",
                    null,
                    IsFatal: true));
            }
        }

        foreach (var column in actual.Columns.Where(column => expected.ColumnNamed(column.Name) is null))
        {
            // 多一列本身不算错（旧版程序回滚上来就是这副样子，EF 只按列名取值）。
            // 唯一真正的坑是 **NOT NULL 且没有默认值**：它会让这张表的 INSERT 被 SQLite 当场拒掉，
            // 而症状只表现为"某个功能失败"（E49 在真机上咬到的就是这一条）。
            var breaksInsert = column.NotNull && !column.HasDefault;
            deviations.Add(new SchemaDeviation(
                SchemaDeviationKind.UnexpectedColumn,
                $"{expected.Name}.{column.Name}",
                breaksInsert
                    ? "模型里没有这一列，而它是 NOT NULL 且没有默认值——留着会让这张表的 INSERT 被 SQLite 拒掉"
                    : "模型里没有这一列（不算错：回滚到旧版就是这副样子）",
                null,
                IsFatal: breaksInsert));
        }

        if (!expected.PrimaryKey.SequenceEqual(actual.PrimaryKey, StringComparer.OrdinalIgnoreCase))
        {
            deviations.Add(new SchemaDeviation(
                SchemaDeviationKind.PrimaryKeyMismatch,
                expected.Name,
                $"主键不符：库里是 ({string.Join(",", actual.PrimaryKey)})，模型要 ({string.Join(",", expected.PrimaryKey)})",
                null,
                IsFatal: true));
        }
    }

    /// <summary>逐个索引比：缺了或形状不符都给得出重建语句（索引里没有数据，重建安全）。</summary>
    private static void CompareIndexes(
        DatabaseSchema.Table expected,
        DatabaseSchema.Table actual,
        List<SchemaDeviation> deviations)
    {
        foreach (var index in expected.Indexes)
        {
            var live = actual.IndexNamed(index.Name);
            if (live is null)
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.MissingIndex,
                    $"{expected.Name}.{index.Name}",
                    index.IsUnique ? "缺一条唯一索引（唯一性因此没人执行）" : "缺一条索引（查询会退化成全表扫）",
                    CreateIndexSql(expected.Name, index),
                    IsFatal: false));
                continue;
            }

            if (live.IsUnique != index.IsUnique
                || !live.Columns.SequenceEqual(index.Columns, StringComparer.OrdinalIgnoreCase))
            {
                deviations.Add(new SchemaDeviation(
                    SchemaDeviationKind.IndexShapeMismatch,
                    $"{expected.Name}.{index.Name}",
                    $"索引形状不符：库里是{(live.IsUnique ? "唯一" : "普通")}索引 ({string.Join(",", live.Columns)})，"
                    + $"模型要{(index.IsUnique ? "唯一" : "普通")}索引 ({string.Join(",", index.Columns)})",
                    DropIndexSql(index.Name) + Environment.NewLine + CreateIndexSql(expected.Name, index),
                    IsFatal: false));
            }
        }

        foreach (var index in actual.Indexes.Where(index => expected.IndexNamed(index.Name) is null))
        {
            deviations.Add(new SchemaDeviation(
                SchemaDeviationKind.UnexpectedIndex,
                $"{expected.Name}.{index.Name}",
                "模型里没有这条索引（不算错，只占一点写入开销）",
                null,
                IsFatal: false));
        }
    }

    /// <summary>建索引语句（<c>IF NOT EXISTS</c>：自愈可能被并发跑两次，第二次该是无操作）。</summary>
    private static string CreateIndexSql(string table, DatabaseSchema.Index index) =>
        $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS \"{index.Name}\" "
        + $"ON \"{table}\" ({string.Join(", ", index.Columns.Select(column => $"\"{column}\""))});";

    /// <summary>删索引语句（只用于"同名但形状不符"：建之前先把旧的那条拿掉）。</summary>
    private static string DropIndexSql(string index) => $"DROP INDEX IF EXISTS \"{index}\";";

    /// <summary>把可能为空的声明类型说成一句人话。</summary>
    private static string Describe(string declaredType) => string.IsNullOrEmpty(declaredType) ? "（无类型）" : declaredType;
}
