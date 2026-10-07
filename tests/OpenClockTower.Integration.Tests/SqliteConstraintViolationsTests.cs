using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 约束类失败的判定（<see cref="SqliteConstraintViolations"/>）：**"有人占了"** 与 **"别写坏了"**
/// 必须分得开——分不开的话，撞上唯一索引却复核不到占用（占用方在复核前退场）那种交错
/// 会变成 Hub 上的未预期异常，而不是一条可重试的业务结论。
/// </summary>
public sealed class SqliteConstraintViolationsTests
{
    /// <summary>SQLITE_CONSTRAINT（19）⇒ 认定是约束挡下的。</summary>
    [Fact]
    public void ConstraintErrorCode_IsRecognized()
    {
        var exception = new DbUpdateException(
            "写失败",
            new SqliteException("UNIQUE constraint failed", SqliteConstraintViolations.ConstraintErrorCode));

        Assert.True(SqliteConstraintViolations.IsConstraintViolation(exception));
    }

    /// <summary>别的错误码（库被锁 / 只读 / 库损坏…）⇒ 不是约束，调用方必须原样抛。</summary>
    /// <param name="errorCode">SQLite 主错误码。</param>
    [Theory]
    [InlineData(5)]  // SQLITE_BUSY
    [InlineData(8)]  // SQLITE_READONLY
    [InlineData(11)] // SQLITE_CORRUPT
    public void OtherErrorCodes_AreNotTreatedAsConstraints(int errorCode)
    {
        var exception = new DbUpdateException("写失败", new SqliteException("boom", errorCode));

        Assert.False(SqliteConstraintViolations.IsConstraintViolation(exception));
    }

    /// <summary>内层不是 SQLite 异常（换了提供程序 / 别的写失败）⇒ 不当成约束。</summary>
    [Fact]
    public void NonSqliteInnerException_IsNotTreatedAsConstraint()
    {
        var exception = new DbUpdateException("写失败", new InvalidOperationException("boom"));

        Assert.False(SqliteConstraintViolations.IsConstraintViolation(exception));
    }
}
