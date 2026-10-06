using System.Text;
using Microsoft.Extensions.Logging;

namespace OpenClockTower.Server;

/// <summary>
/// 库文件级的**单实例锁**（M5 / G-A6-3）：同一份库只允许一个服务进程。
/// </summary>
/// <remarks>
/// <para>
/// 审计的读数：全仓 <c>Mutex</c> / <c>SingleInstance</c> / <c>lockfile</c> / <c>FileShare</c> 零命中，
/// 而误开两个实例的现场是——后起来的那个在补列时吃到 <c>duplicate column name</c>，
/// 被当成"库坏了"，报一句"请换新库"然后启动失败。**误操作被伪装成了数据损坏**，
/// 这是最坏的一种错误信息：它会让人去删库。
/// </para>
/// <para>
/// 做法是库文件旁边一个 <c>&lt;库路径&gt;.lock</c>：以 <c>FileShare.None</c> 打开并一直握着
/// （Windows 上是共享模式，Unix 上 .NET 用 <c>flock</c> 实现，跨进程同样管用）。
/// 拿不到就是"已经有实例在跑"，报一句人话，并**在端口被占用之前**就让进程起不来。
/// </para>
/// <para>
/// **锁文件不删**：正常退出也不删。删它是有害的——释放锁与删除之间，另一个进程可能刚好拿到锁，
/// 于是它握着的是一个已被删除的文件，第三个进程又能建一个新的同名文件并加锁，单实例就不再成立。
/// 停机后这个文件还在是正常的，它只是个空壳（内容仅用于事后判断"上次是谁"）。
/// </para>
/// <para>
/// 刻意**不**给维护命令（<c>backup</c> / <c>db-report</c>）加这把锁：热备份与只读体检本来就该
/// 在服务跑着的时候做，它们走 SQLite 自己的并发规则（在线备份 API / 只读连接）。
/// </para>
/// </remarks>
public sealed class ServerInstanceLock : IDisposable
{
    /// <summary>锁文件名后缀（跟在库路径后面）。</summary>
    public const string FileSuffix = ".lock";

    private readonly FileStream _stream;

    private ServerInstanceLock(FileStream stream) => _stream = stream;

    /// <summary>锁文件路径（库路径 + <see cref="FileSuffix"/>）。</summary>
    public static string PathFor(string databasePath) => Path.GetFullPath(databasePath) + FileSuffix;

    /// <summary>
    /// 拿到这把锁；拿不到就抛（调用方让启动失败，别带着两个实例继续跑）。
    /// </summary>
    /// <param name="databasePath">库文件路径（锁文件与它同目录）。</param>
    /// <param name="logger">启动日志。</param>
    public static ServerInstanceLock Acquire(string databasePath, ILogger logger)
    {
        var lockPath = PathFor(databasePath);
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            var payload = Encoding.UTF8.GetBytes(
                $"pid={Environment.ProcessId} 启动时刻={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            stream.Write(payload);
            stream.Flush();

            logger.LogInformation("单实例锁：{Path}（本进程 {Pid} 持有）", lockPath, Environment.ProcessId);
            return new ServerInstanceLock(stream);
        }
        catch (IOException exception)
        {
            logger.LogCritical(
                exception,
                "另一个实例正拿着这个库，本进程退出：{Path}（库文件级单实例锁，G-A6-3）",
                lockPath);
            throw new InvalidOperationException(
                $"另一个实例正在使用这个库：{lockPath} 被它独占。"
                + "一份库只能有一个服务进程——两个实例同写一个 SQLite 库，写坏的是一整局事件流。"
                + "先确认没有第二个进程在跑（systemctl status <服务名>），再重启本服务。",
                exception);
        }
    }

    /// <summary>释放锁（进程退出时；锁文件本身留着）。</summary>
    public void Dispose() => _stream.Dispose();
}
