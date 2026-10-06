using System.Data;
using System.Data.Common;
using System.Globalization;

namespace OpenClockTower.Server;

/// <summary>
/// 库文件里的**结构版本号**（M5 / G-A6-2）：读写 SQLite 自己的 <c>user_version</c>。
/// </summary>
/// <remarks>
/// <para>
/// 用它而不是自建一张版本表：它是库文件头里的一个整数，SQLite 原生支持，
/// **与 DDL 在同一个事务里生效**（回滚时一起回去），而且备份 / <c>VACUUM</c> 都原样带着它——
/// 一份备份于是自带"我是哪个版本"，恢复回来之后该补的迁移一条都不会漏。
/// </para>
/// <para>
/// 含义只有一个：**这个库的结构已经被哪些迁移改过**。它不表达"最新版本是多少"，
/// 也不表达"程序版本"——那是 <see cref="SchemaMigrationCatalog.LatestVersion"/> 的事，
/// 两者一比就得到"库比程序新"（回滚过的现场）或"还有没跑的迁移"。
/// </para>
/// </remarks>
public static class SqliteUserVersion
{
    /// <summary>读出当前结构版本；从没迁移过的库（含 <c>EnsureCreated</c> 时代的老库）读出来是 0。</summary>
    public static async Task<int> ReadAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 写版本的语句。刻意做成**语句文本**而不是直接执行：它必须与那条迁移的 DDL 在同一个事务里提交。
    /// </summary>
    /// <remarks>取值是 <see cref="int"/>，不拼外部字符串（<c>PRAGMA</c> 也不接受参数占位符）。</remarks>
    public static string Assignment(int version) =>
        $"PRAGMA user_version = {version.ToString(CultureInfo.InvariantCulture)};";
}
