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

    /// <summary>删掉库与它的全部伴随文件；**返回删不掉的那些**（空 = 干净）。</summary>
    internal static IReadOnlyList<string> Delete(string databasePath)
    {
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
