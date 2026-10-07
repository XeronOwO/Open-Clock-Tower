using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OpenClockTower.Server;

/// <summary>
/// 把"这次写入是被**约束**挡下的"从 <see cref="DbUpdateException"/> 里认出来。
/// </summary>
/// <remarks>
/// <para>
/// 存储层用唯一索引执行不变量（一席一账号 / 一账号一席 / 登录名唯一）。撞上约束之后上层要能区分
/// 两件事：**"有人占了"**（业务拒绝）与**"别写坏了"**（原样抛）。两者之间的桥是"复核读一次"。
/// </para>
/// <para>
/// 但复核也可能读不到——那种交错里占用方**已经退场**（并发解除 / 注销），于是复核为空、
/// 异常却被原样抛出，上层连收敛成可重试结论的机会都没有。这时唯一可靠的判据是**错误码本身**：
/// SQLITE_CONSTRAINT（19）说明这次写确实是被约束挡下的，不是磁盘或别的问题。
/// </para>
/// </remarks>
public static class SqliteConstraintViolations
{
    /// <summary>SQLITE_CONSTRAINT：主键 / 唯一 / 非空 / 外键等约束类失败。</summary>
    public const int ConstraintErrorCode = 19;

    /// <summary>这次失败是不是约束挡下的。</summary>
    /// <param name="exception">EF 抛出的更新失败。</param>
    public static bool IsConstraintViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is SqliteException { SqliteErrorCode: ConstraintErrorCode };
    }
}
