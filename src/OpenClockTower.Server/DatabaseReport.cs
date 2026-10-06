using System.Globalization;

namespace OpenClockTower.Server;

/// <summary>
/// 一次**只读体检**的读数（M5 / G-A6-4）：备份能不能用、库里有什么，都从这一个对象读。
/// </summary>
/// <remarks>
/// 维护命令（<c>db-report</c>）把它打给人看；备份命令（<c>backup</c>）用同一次体检验证刚写出来的备份。
/// 两处共用同一个读数，就不会出现"备份报成功、恢复时报坏"这种两头不一致的情况。
/// </remarks>
/// <param name="Path">被检查的文件。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="Sha256">整文件 SHA-256（小写十六进制）——异地里核对"传过去的那份是不是这一份"。</param>
/// <param name="Integrity">SQLite <c>integrity_check</c> 的结论（<c>ok</c> 才是好）。</param>
/// <param name="JournalMode">日志模式（库文件里的持久属性）。</param>
/// <param name="PageSize">页大小（字节）。</param>
/// <param name="PageCount">页数。</param>
/// <param name="FreelistCount">空闲页数（不占内容、但占文件）。</param>
/// <param name="Tables">各表行数。</param>
/// <param name="Games">逐局读数。</param>
public sealed record DatabaseReport(
    string Path,
    long SizeBytes,
    string Sha256,
    string Integrity,
    string JournalMode,
    int PageSize,
    int PageCount,
    int FreelistCount,
    DatabaseReport.TableCounts Tables,
    IReadOnlyList<DatabaseReport.GameLine> Games)
{
    /// <summary>各表的行数。</summary>
    public sealed record TableCounts(int Games, int Events, int Users, int Snapshots, int Receipts, int SeatBindings);

    /// <summary>一局的读数：桌名、席位数、事件条数与最后序号。</summary>
    public sealed record GameLine(string GameId, string Name, int SeatCount, int Events, long LastSequence);

    /// <summary>把读数写成给人看的多行文本（维护命令与批次记录共用同一种表述）。</summary>
    public string Describe()
    {
        var lines = new List<string>
        {
            $"文件：{Path}（{SizeBytes.ToString("N0", CultureInfo.InvariantCulture)} 字节 · SHA-256 {Sha256}）",
            $"体检：完整性={Integrity} · 日志模式={JournalMode} · 页大小={PageSize} · 页数={PageCount} · 空闲页={FreelistCount}",
            $"内容：桌={Tables.Games} · 事件={Tables.Events} · 账号={Tables.Users} · 快照={Tables.Snapshots}"
            + $" · 回执={Tables.Receipts} · 席位绑定={Tables.SeatBindings}",
        };

        foreach (var game in Games)
        {
            lines.Add(
                $"某一局：标识={game.GameId} · 桌名={game.Name} · 席位={game.SeatCount}"
                + $" · 事件={game.Events} · 最后序号={game.LastSequence}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
