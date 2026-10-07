namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// SQLite 连接池作用域门禁：<c>src/</c> 与 <c>tests/</c> 里不得出现**进程级清池**。
/// </summary>
/// <remarks>
/// <para>
/// 依据是一次真实的随机红（2026-10-08，票 <c>done/integration-suite-parallel-flakes.md</c>）：
/// <c>SqliteConnection.ClearAllPools()</c> 不只是清空闲句柄——它内部会走
/// <c>ReclaimLeakedConnections()</c>，把**已经借出去、正被别的线程使用**的连接也一并回收。
/// 集成用例默认按集合并行（同进程几十台宿主），于是任何一处收尾都会拆掉别人手里正在用的
/// <c>sqlite3</c> 句柄，症状是一批毫不相干的用例随机红：
/// <c>ObjectDisposedException: SQLitePCL.sqlite3</c> 与 <c>SQLite Error 5: 'database is locked'</c>。
/// 实测栈：<c>ClearAllPools → SqliteConnectionFactory.ClearPools → SqliteConnectionPool.Clear →
/// ReclaimLeakedConnections → Return → Deactivate</c>。
/// </para>
/// <para>
/// 正路是**按库清池**：<c>SqliteConnection.ClearPool</c>；测试侧的出口是
/// <c>TestDatabaseFiles.ReleasePool(库路径)</c>（连接串取自产品侧的 <c>SqliteConnectionStrings.ForPath</c>，
/// 与宿主同一个字符串）。这条门禁的作用是让"图省事写回全进程清池"必须显式改测试，
/// 而不是在下一次并行跑里随机咬人。
/// </para>
/// <para>
/// 判的是**调用**而不是**提到**：文本先过 <see cref="SourceText.StripCommentsAndLiterals"/>，
/// 于是注释与文档里写这个名字（说明为什么不用它）不会算违规——假红会让门禁失去信任。
/// </para>
/// </remarks>
public sealed class SqlitePoolScopeGateTests
{
    /// <summary>被禁的调用形状（带左括号：避免把纯类型名 / 别的方法名一并算上）。</summary>
    private const string ForbiddenCall = "ClearAllPools(";

    [Fact]
    public void Sources_NeverClearEveryConnectionPoolInTheProcess()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src")
            .Concat(RepositoryLayout.EnumerateSourceFiles("tests"))
            .ToList();
        Assert.NotEmpty(files);

        var violations = files
            .Where(path => SourceText
                .StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(path)))
                .Contains(ForbiddenCall, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "出现了进程级清池：它会回收别的线程**正在用**的连接，并行用例会随机红。"
            + Environment.NewLine
            + "改成按库清池——产品侧 SqliteConnection.ClearPool(...)，测试侧 TestDatabaseFiles.ReleasePool(库路径)。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
