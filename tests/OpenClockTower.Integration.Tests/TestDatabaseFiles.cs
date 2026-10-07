using Microsoft.Data.Sqlite;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 临时库的**伴随文件**清单（一个库 = 一组文件），以及把它们一起删掉的那一个出口。
/// </summary>
/// <remarks>
/// <para>
/// 一个 SQLite 库在磁盘上从来不是一个文件：WAL 模式有 <c>-wal</c> / <c>-shm</c>，
/// 而 M5 / G-A6-3 之后又多了一个 <c>.lock</c>（库文件级单实例锁）。
/// 这些名字此前散在十几个用例的收尾代码里各写一遍——**产品多一个伴随文件就要改十几处**，
/// 漏掉的那处会在 <c>%TEMP%</c> 里留下垃圾（实测：一轮用例留下 574 个 <c>.lock</c>）。
/// </para>
/// <para>
/// 于是收成一个出口：清单只在这里维护。锁文件后缀刻意**取产品侧的常量**
/// （<see cref="ServerInstanceLock.FileSuffix"/>）而不是抄一遍字符串——抄一遍就等于又埋了一次
/// "改了产品忘了改用例"。
/// </para>
/// <para>
/// 两档口径：<see cref="Delete"/> 尽力而为（用完即弃的临时库，删不掉留给收尾），
/// <see cref="DeleteOrFail"/> 断言删干净（那些本来就以"不留残渣"为判据的用例）。
/// </para>
/// </remarks>
internal static class TestDatabaseFiles
{
    /// <summary>库文件之后可能跟着的伴随文件后缀。</summary>
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", ServerInstanceLock.FileSuffix];

    /// <summary>库与它的全部伴随文件（无论存不存在，用来删或用来核对）。</summary>
    internal static IReadOnlyList<string> PathsOf(string databasePath) =>
        [databasePath, .. SidecarSuffixes.Select(suffix => databasePath + suffix)];

    /// <summary>
    /// 释放**这一个库**在连接池里的句柄。
    /// </summary>
    /// <param name="databasePath">库文件路径（与宿主配置的 <c>GameServer:DatabasePath</c> 同一个）。</param>
    /// <remarks>
    /// <para>
    /// **刻意不用 <c>SqliteConnection.ClearAllPools()</c>**——它是**进程级**的：按库清池只动一个
    /// 连接串的池，反向就是"它一次动**全进程所有库**的池"。集成用例默认按集合并行（几十台宿主同进程），
    /// 于是任何一台宿主收尾都会动到别的用例正在用的池，症状是一批毫不相干的用例随机红：
    /// <c>ObjectDisposedException: SQLitePCL.sqlite3</c>（句柄在受害者**自己的**操作中途被拆：
    /// <c>sqlite3_prepare_v2</c> / <c>SqliteConnection.Open()</c> / <c>SaveChangesAsync</c> /
    /// <c>BackupDatabase</c>）与 <c>SQLite Error 5: 'database is locked'</c>（清池路径自己抛，
    /// 栈：<c>ClearAllPools → ClearPools → Clear → ReclaimLeakedConnections → Return → Deactivate</c>）。
    /// </para>
    /// <para>
    /// 边界：<c>ReclaimLeakedConnections()</c> 只说明它回收"它认为泄漏的"连接；
    /// "被回收的正是别的线程**手里正在用**的那条"是推断——一次两线程最小探针没能单独复现，
    /// 只在整解决方案并行下出现。修法不依赖这条强机制。
    /// </para>
    /// <para>
    /// 按库清池只动这一个连接串的池（实测：清 A 库之后 A 的文件可删、B 库的池内句柄照旧握着），
    /// 而并行用例之间**库路径互不相同**，于是它不会碰到任何别的用例。
    /// 连接串取自 <see cref="SqliteConnectionStrings.ForPath"/>——与宿主的连接串是同一处事实，
    /// 否则清的是一个空池（**池键就是连接串字面量**）。
    /// </para>
    /// </remarks>
    internal static void ReleasePool(string databasePath) =>
        SqliteConnection.ClearPool(new SqliteConnection(SqliteConnectionStrings.ForPath(databasePath)));

    /// <summary>删掉库与它的全部伴随文件；**返回删不掉的那些**（空 = 干净）。</summary>
    internal static IReadOnlyList<string> Delete(string databasePath)
    {
        // 先放掉池里的句柄：Windows 上句柄还开着就删不掉（那会变成"收尾假红"）。
        ReleasePool(databasePath);

        var remaining = new List<string>();
        foreach (var path in PathsOf(databasePath))
        {
            if (File.Exists(path) && !TryDelete(path))
            {
                remaining.Add(path);
            }
        }

        return remaining;
    }

    /// <summary>删干净，删不掉就让用例失败——测试留下的残渣必须被看见，而不是留给用户去清。</summary>
    internal static void DeleteOrFail(string databasePath)
    {
        var remaining = Delete(databasePath);
        Assert.True(
            remaining.Count == 0,
            $"测试残留文件删不掉，需要收尾清理：{string.Join("、", remaining)}");
    }

    /// <summary>
    /// 删一个文件，短暂重试几次。
    /// </summary>
    /// <remarks>Windows 上刚关掉的句柄可能还没完全落地，紧接着删会偶发 sharing violation——那是假红。</remarks>
    private static bool TryDelete(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(20);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }
}
