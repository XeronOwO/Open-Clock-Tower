namespace OpenClockTower.Server;

/// <summary>
/// SQLite 连接口径（M5 / G-A6-8）：**只在配置里出现一次**，启动时打进日志。
/// </summary>
/// <remarks>
/// 日志模式与同步级别**刻意不做成旋钮**（写在 <see cref="SqliteConnectionPragmas"/> 的常量里）：
/// 它们决定"掉电会不会丢已经提交的事件"，不该被随手调小。这里可配的只有"等锁等多久"——
/// 那是个与部署规模有关的量：单进程自用可以短，运维同机用 <c>sqlite3</c> 写库时会希望它长一点。
/// </remarks>
public sealed class SqliteOptions
{
    /// <summary>配置节名（挂在 <c>GameServer</c> 之下）。</summary>
    public const string SectionName = "GameServer:Sqlite";

    /// <summary>
    /// 写锁等待上限（毫秒），默认 5000。
    /// </summary>
    /// <remarks>
    /// 这正是审计读数里那个 <c>busy_timeout = 0</c> 的另一半：0 表示"一撞上写锁就失败"，
    /// 而本项目有节拍器、多个 Hub 调用与可能同机跑着的运维命令，撞锁是常态而不是异常。
    /// 5 秒足够覆盖一次普通写入的排队，又不会把请求挂死。
    /// </remarks>
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
}
