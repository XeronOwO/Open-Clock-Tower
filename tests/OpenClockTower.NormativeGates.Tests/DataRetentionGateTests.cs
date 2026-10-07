namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 数据删除路径与保留策略门禁（M5 / G-A6-5 · G-A1-6）：删库只有两个出口，留多久必须写给人看。
/// </summary>
/// <remarks>
/// <para>
/// 第一半是机制：删数据的语句只能出现在那两个存储实现里。审计的原话是"库层没有任何删除路径"——
/// 补上它之后，**风险从"删不掉"翻到了"到处都能删"**：多一个删除入口就多一条可能删错的路，
/// 而删除是不可逆的。白名单是双向判的（见 <see cref="DeletionLivesOnlyInTheTwoStores"/>）：
/// 名单里的文件若一处删除都没有了，也报红——陈旧的豁免会替将来某段删除挡住门禁。
/// </para>
/// <para>
/// 第二半是口径：陌生人唯一的信息来源是部署文档。"留多久、什么时候会没、怎么手工删、注销删到什么程度"
/// 这几条少一条，他就得靠猜——而猜错的代价是数据没了。判的是**给人看的那句话**，不是小节标题。
/// </para>
/// </remarks>
public sealed class DataRetentionGateTests
{
    /// <summary>
    /// 允许删库里数据的文件——**每一条都要写清理由**。
    /// </summary>
    private static readonly (string File, string Reason)[] DeletionHomes =
    [
        ("src/OpenClockTower.Server/EfTableRetirementStore.cs", "桌退役：五张表联删 + 孤儿行清理（M5 / G-A6-5）"),
        ("src/OpenClockTower.Server/EfAccountErasureStore.cs", "账号注销：账号行 + 席位绑定 + 归属置空（M5 / G-A1-6）"),
    ];

    /// <summary>删除的关键词：原生 SQL 与 EF 的两个动词都算（少一个都算这条门禁没覆盖到）。</summary>
    private static readonly string[] DeletionMarkers = ["DELETE FROM", "ExecuteDelete"];

    private static string DeployDocument =>
        File.ReadAllText(RepositoryLayout.PathOf("docs", "operations", "deploy.md"));

    /// <summary>
    /// 产品代码里只有那两个存储能删库里的数据，而且这两个文件必须真的在删。
    /// </summary>
    /// <remarks>
    /// 扫的是**去掉注释之后**的源码：注释里提到"删行"不该报红（那是给人看的解释），
    /// 而写在字符串里的 <c>DELETE FROM</c> 一定要命中——它恰恰是本项目放原生 SQL 的形态。
    /// 用例（<c>tests/</c>）不在此列：那里的删除是**判据的一部分**（故意清库、故意造残渣）。
    /// </remarks>
    [Fact]
    public void DeletionLivesOnlyInTheTwoStores()
    {
        var offenders = new List<string>();
        var homes = DeletionHomes.Select(home => Normalize(home.File)).ToHashSet(StringComparer.Ordinal);
        var homesWithDeletion = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relativePath in RepositoryLayout.EnumerateSourceFiles("src"))
        {
            var text = SourceText.StripComments(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            var found = DeletionMarkers
                .Where(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (found.Count == 0)
            {
                continue;
            }

            var normalized = Normalize(relativePath);
            if (homes.Contains(normalized))
            {
                homesWithDeletion.Add(normalized);
                continue;
            }

            offenders.Add($"{relativePath} → {string.Join("、", found)}");
        }

        Assert.True(
            offenders.Count == 0,
            "删库里的数据只有那两个出口（M5 / G-A6-5）：" + Environment.NewLine
            + string.Join(Environment.NewLine, DeletionHomes.Select(home => $"  · {home.File}：{home.Reason}"))
            + Environment.NewLine + "越界的文件：" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders) + Environment.NewLine
            + "删除是不可逆的：要删数据就往那两个口子里加一条（顺带想清楚谁受影响、留下什么审计），"
            + "不要另起一条路。");

        var stale = DeletionHomes
            .Where(home => !homesWithDeletion.Contains(Normalize(home.File)))
            .Select(home => home.File)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "这几条删除白名单已经失效（文件里一处删除都没有了）：" + Environment.NewLine
            + string.Join(Environment.NewLine, stale) + Environment.NewLine
            + "白名单是双向判的：不再需要豁免就把它删掉，别留在这里替将来的删除挡门禁。");
    }

    /// <summary>
    /// 部署文档要把**保留期限、回收口径、手工回收命令、注销删到什么程度**写出来。
    /// </summary>
    /// <remarks>
    /// 断言刻意锚在"给人看的那句话"上（而不是只锚小节标题）：标题还在、内容被搬走的情况完全可能发生，
    /// 那时门禁不该假绿。这一组也是隐私说明（G-A8-4）的答案来源——"数据留多久"必须先有个真实的口径。
    /// </remarks>
    [Fact]
    public void DeployDocument_DocumentsRetentionAndErasure()
    {
        var document = DeployDocument;
        string[] required =
        [
            // 保留期限：两档默认值与它们各自管什么
            "### 9.4 数据保留与空闲桌回收",
            "GameServer__TableRetention__EmptyTableHours",
            "GameServer__TableRetention__PlayedTableDays",
            "从未开局的桌",
            "开过局的桌",
            // "多久没动"怎么算，以及**没有依据就不删**
            "无法判定",
            "有在线连接",
            // 手工回收怎么用（默认只报告、写操作要独占）
            "retire-tables --apply",
            "只报告",
            // 注销：删到什么程度、要口令二次确认、不可逆
            "### 9.6 注销账号",
            "口令二次确认",
            "不可逆",
        ];

        var missing = required.Where(line => !document.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "部署文档少了保留期限 / 回收 / 注销的口径（M5）：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    /// <summary>门禁自己的路径比较要跨平台（比较用正斜杠，免得 Windows 上永远不相等）。</summary>
    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
}
