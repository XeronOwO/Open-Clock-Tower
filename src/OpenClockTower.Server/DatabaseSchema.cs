using System.Text;

namespace OpenClockTower.Server;

/// <summary>
/// 一个库的**结构快照**（M5 / G-A6-1）：表、列（类型与 NOT NULL）、主键位、索引。
/// </summary>
/// <remarks>
/// <para>
/// 一个形状、两个来源：<see cref="SqliteSchemaReader"/> 从库里读出来，<see cref="SchemaContract"/> 从 EF 模型算出来。
/// 两者用同一个类型表达，才谈得上逐项比对——审计的原话是"索引与约束无人验证"，
/// 而"没人验证"的根因正是**没有一种可比较的形状**（旧守卫只认列名，索引与约束根本不在它的视野里）。
/// </para>
/// <para>
/// **刻意不比默认值**：老库的 <c>Name</c> / <c>IsLocked</c> 是上一版用 <c>ADD COLUMN ... DEFAULT</c> 补的，
/// 新库里这两列没有默认值——这是两次"合法但写法不同"的建表留下的差异，不影响行为
/// （所有写入都由 EF 按列名显式给值，默认值永远轮不到）。列序同理不比：SQLite 按列名取值。
/// </para>
/// </remarks>
/// <param name="Tables">全部用户表（不含 <c>sqlite_%</c> 那几张 SQLite 自己的表）。</param>
public sealed record DatabaseSchema(IReadOnlyList<DatabaseSchema.Table> Tables)
{
    /// <summary>一张表的形状。</summary>
    /// <param name="Name">表名。</param>
    /// <param name="Columns">列（比对时按名字排序，列序不参与）。</param>
    /// <param name="Indexes">**显式建出来的**索引（<c>origin='c'</c>）：唯一约束与主键自带的自动索引不算。</param>
    /// <param name="PrimaryKey">主键列，按主键里的次序排列。</param>
    public sealed record Table(
        string Name,
        IReadOnlyList<DatabaseSchema.Column> Columns,
        IReadOnlyList<DatabaseSchema.Index> Indexes,
        IReadOnlyList<string> PrimaryKey)
    {
        /// <summary>按名字找一列；没有就是 null。</summary>
        public DatabaseSchema.Column? ColumnNamed(string name) =>
            Columns.FirstOrDefault(column => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>按名字找一个索引；没有就是 null。</summary>
        public DatabaseSchema.Index? IndexNamed(string name) =>
            Indexes.FirstOrDefault(index => string.Equals(index.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>一列的形状。</summary>
    /// <param name="Name">列名。</param>
    /// <param name="DeclaredType">声明类型（<c>TEXT</c> / <c>INTEGER</c> …；SQLite 是动态类型，但声明类型决定类型亲和性）。</param>
    /// <param name="NotNull">是否声明了 <c>NOT NULL</c>。</param>
    /// <param name="HasDefault">
    /// 有没有默认值。**不参与形状比对**（老库那两列是 <c>ADD COLUMN ... DEFAULT</c> 补的，新库没有默认值，
    /// 这是两次合法建表留下的差异，进比对只会制造假差异）；它只服务一条判据：
    /// **模型里没有的列，若还是 <c>NOT NULL</c> 且没有默认值，这张表的 INSERT 会被 SQLite 拒掉**
    /// （E49 在真机上咬到过这条：`NOT NULL constraint failed: Games.StorytellerTicket`，
    /// 界面上只表现为"开新桌失败"）。
    /// </param>
    /// <param name="PrimaryKeyOrdinal">在主键里的位次（1 起；0 = 不是主键）。</param>
    public sealed record Column(string Name, string DeclaredType, bool NotNull, bool HasDefault, int PrimaryKeyOrdinal);

    /// <summary>一个显式索引的形状。</summary>
    /// <param name="Name">索引名（EF 的约定是 <c>IX_表_列…</c>）。</param>
    /// <param name="Columns">索引列，按索引里的次序排列。</param>
    /// <param name="IsUnique">是否唯一索引——这两条唯一索引是"一号一人 / 一席一人"的**唯一**执行者。</param>
    public sealed record Index(string Name, IReadOnlyList<string> Columns, bool IsUnique);

    /// <summary>有没有这张表。</summary>
    public bool HasTable(string name) => TableNamed(name) is not null;

    /// <summary>有没有这一列（表不存在也算没有）。</summary>
    public bool HasColumn(string table, string column) => TableNamed(table)?.ColumnNamed(column) is not null;

    /// <summary>按名字找一张表；没有就是 null。</summary>
    public DatabaseSchema.Table? TableNamed(string name) =>
        Tables.FirstOrDefault(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 把整份结构渲染成**稳定排序**的多行文本：两个库同形 ⇔ 两段文本逐字相同。
    /// </summary>
    /// <remarks>
    /// 用途是"形状一模一样吗"这种判据（老库升级后 vs 新库、迁移建出来的 vs EF 模型建出来的）：
    /// 比直接比对象可靠——记录里的列表是引用比较，而这里逐个字段落成文字，差一格都看得见。
    /// </remarks>
    public string ToCanonicalText()
    {
        var text = new StringBuilder();
        foreach (var table in Tables.OrderBy(table => table.Name, StringComparer.Ordinal))
        {
            text.Append("表 ").Append(table.Name).AppendLine();
            foreach (var column in table.Columns.OrderBy(column => column.Name, StringComparer.Ordinal))
            {
                text.Append("  列 ").Append(column.Name)
                    .Append(' ').Append(column.DeclaredType)
                    .Append(" notnull=").Append(column.NotNull ? '1' : '0')
                    .Append(" pk=").Append(column.PrimaryKeyOrdinal)
                    .AppendLine();
            }

            foreach (var index in table.Indexes.OrderBy(index => index.Name, StringComparer.Ordinal))
            {
                text.Append("  索引 ").Append(index.Name)
                    .Append(" unique=").Append(index.IsUnique ? '1' : '0')
                    .Append(" (").Append(string.Join(",", index.Columns)).Append(')')
                    .AppendLine();
            }

            text.Append("  主键 (").Append(string.Join(",", table.PrimaryKey)).Append(')').AppendLine();
        }

        return text.ToString();
    }
}
