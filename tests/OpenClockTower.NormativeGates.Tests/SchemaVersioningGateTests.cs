namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 结构版本与迁移门禁（M5 / G-A6-1 · G-A6-2 · G-A6-3）：DDL 只有一个出口，口径必须写给人看。
/// </summary>
/// <remarks>
/// <para>
/// 这两条守的是**同一件事的两半**。第一半是机制：建表 / 补列 / 建索引的语句只能出现在迁移清单里，
/// "启动守卫"那种"每加一列补一行、索引与约束没人问"的写法不许长回来（审计 G-A6-1 的成因）。
/// 第二半是口径：陌生人唯一的信息来源是部署文档，结构版本、拒绝启动、回滚前提、锁文件
/// 这几条少一条，他就得靠猜——而猜错的代价是删库。
/// </para>
/// <para>
/// 判的是**文本存在性**，不判 SQL 的语法或运行行为（那是集成用例与真机读数的事）。
/// 存在性门禁的价值在于：删掉某一条时，必须在测试里显式删掉这一行——那时他至少会看见"这是有意为之的"。
/// </para>
/// </remarks>
public sealed class SchemaVersioningGateTests
{
    /// <summary>迁移清单：本仓库里**唯一**允许出现 DDL 的文件。</summary>
    private const string CatalogFile = "src/OpenClockTower.Server/SchemaMigrationCatalog.cs";

    /// <summary>
    /// 允许写 DDL 的文件白名单——**每一条都要写清理由**，理由不成立的豁免就是破窗。
    /// </summary>
    /// <remarks>
    /// 白名单是双向判的（见 <see cref="DdlLivesOnlyInTheMigrationCatalog"/>）：名单里的文件
    /// 若一处 DDL 都没有，也报红——否则陈旧的豁免会静静地留在那里，替将来某段 DDL 挡住门禁。
    /// </remarks>
    private static readonly (string File, string Reason)[] DdlHomes =
    [
        (CatalogFile, "结构演进：建表 / 补列 / 建回索引 / 清掉退场列（M5 / G-A6-2）"),
        ("src/OpenClockTower.Server/SchemaComparer.cs", "结构自愈：缺索引或索引形状不对时给出重建语句（M5 / G-A6-1）"),
    ];

    /// <summary>结构变更的关键词（少一个都算这条门禁没覆盖到）。</summary>
    private static readonly string[] DdlMarkers =
    [
        "CREATE TABLE",
        "ALTER TABLE",
        "DROP TABLE",
        "CREATE INDEX",
        "CREATE UNIQUE INDEX",
        "DROP INDEX",
        "DROP COLUMN",
    ];

    private static string DeployDocument =>
        File.ReadAllText(RepositoryLayout.PathOf("docs", "operations", "deploy.md"));

    /// <summary>
    /// 产品代码里只有白名单那两个文件能写 DDL，而且这两个文件必须真的在写。
    /// </summary>
    /// <remarks>
    /// 扫的是**去掉注释之后**的源码：注释里提到 DDL 关键词不该报红（那是给人看的解释），
    /// 但写在字符串里的 DDL 一定要命中——它恰恰是本项目放 DDL 的形态（原生字符串里的建表语句）。
    /// 用例（`tests/`）不在此列：那里的 DDL 是**判据的一部分**（故意把库改坏，看守卫报不报）。
    /// </remarks>
    [Fact]
    public void DdlLivesOnlyInTheMigrationCatalog()
    {
        var offenders = new List<string>();
        var homes = DdlHomes.Select(home => Normalize(home.File)).ToHashSet(StringComparer.Ordinal);
        var homesWithDdl = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relativePath in RepositoryLayout.EnumerateSourceFiles("src"))
        {
            var text = SourceText.StripComments(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            var found = DdlMarkers.Where(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0)
            {
                continue;
            }

            var normalized = Normalize(relativePath);
            if (homes.Contains(normalized))
            {
                homesWithDdl.Add(normalized);
                continue;
            }

            offenders.Add($"{relativePath} → {string.Join("、", found)}");
        }

        Assert.True(
            offenders.Count == 0,
            "产品代码里只有这两处能写 DDL（M5 / G-A6-2）：" + Environment.NewLine
            + string.Join(Environment.NewLine, DdlHomes.Select(home => $"  · {home.File}：{home.Reason}"))
            + Environment.NewLine + "越界的文件：" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders) + Environment.NewLine
            + "要改结构就往迁移清单里加一条（守卫式：先看现状再动手），不要另起一条改库的路。");

        var stale = DdlHomes
            .Where(home => !homesWithDdl.Contains(Normalize(home.File)))
            .Select(home => home.File)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "这几条 DDL 白名单已经失效（文件里一处 DDL 都没有了）：" + Environment.NewLine
            + string.Join(Environment.NewLine, stale) + Environment.NewLine
            + "白名单是双向判的：不再需要豁免就把它删掉，别留在这里替将来的 DDL 挡门禁。");
    }

    /// <summary>
    /// 部署文档要把结构版本、自动迁移、拒绝启动、回滚前提与锁文件写出来。
    /// </summary>
    /// <remarks>
    /// 断言刻意锚在**给人看的那句话**上（而不是只锚小节标题）：标题还在、内容被搬走的情况
    /// 完全可能发生，那时门禁不该假绿。
    /// </remarks>
    [Fact]
    public void DeployDocument_DocumentsSchemaVersioningAndRollback()
    {
        var document = DeployDocument;
        string[] required =
        [
            // 版本号在哪、谁改它
            "### 6.6 结构版本与迁移（口径）",
            "user_version",
            "**启动时自动迁移**",
            "一条迁移一个事务",
            // 两种拒绝启动的情形（版本闸 + 结构核对）
            "**拒绝启动**",
            "当场**自愈**",
            // 回滚：先看版本，再决定要不要连库一起回
            "### 9.3 回滚",
            "动手之前先看结构版本",
            "PRAGMA user_version=",
            // 单实例锁文件（停机后还在是正常的，别删）
            "### 9.5 单实例：库旁边那个 `.lock` 文件",
            "**不要删它**",
        ];

        var missing = required.Where(line => !document.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "部署文档少了结构版本 / 迁移 / 回滚 / 单实例锁的口径（M5）：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    /// <summary>门禁自己的路径比较要跨平台（比较用正斜杠，免得 Windows 上永远不相等）。</summary>
    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
}
