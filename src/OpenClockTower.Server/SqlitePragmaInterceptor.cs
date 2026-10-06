using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenClockTower.Server;

/// <summary>
/// 把连接级 SQLite 口径（同步级别 + 写锁等待）应用到**每一个**打开的连接上（M5 / G-A6-8）。
/// </summary>
/// <remarks>
/// 这两条是连接级而不是库级的属性，所以"设一次"是不够的——EF 的连接池会在任意时刻给出
/// 一条新连接。挂在拦截器上，就与"谁开的连接"无关：应用自己的库、维护命令、将来的后台任务
/// 走的都是同一条路。
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private readonly SqliteOptions _options;

    /// <summary>构造拦截器。</summary>
    public SqlitePragmaInterceptor(SqliteOptions options) => _options = options;

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SqliteConnectionPragmas.ApplyPerConnection(connection, _options);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SqliteConnectionPragmas.ApplyPerConnectionAsync(connection, _options, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
